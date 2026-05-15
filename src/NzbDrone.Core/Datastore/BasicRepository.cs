using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Dapper;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Datastore
{
    public interface IBasicRepository<TModel>
        where TModel : ModelBase, new()
    {
        IEnumerable<TModel> All();
        int Count();
        TModel Find(int id);
        TModel Get(int id);
        TModel Insert(TModel model);
        TModel Update(TModel model);
        TModel Upsert(TModel model);
        void SetFields(TModel model, params Expression<Func<TModel, object>>[] properties);
        void Delete(TModel model);
        void Delete(int id);
        IEnumerable<TModel> Get(IEnumerable<int> ids);
        void InsertMany(IList<TModel> model);
        void UpdateMany(IList<TModel> model);
        void SetFields(IList<TModel> models, params Expression<Func<TModel, object>>[] properties);
        void DeleteMany(List<TModel> model);
        void DeleteMany(IEnumerable<int> ids);
        void Purge(bool vacuum = false);
        bool HasItems();
        TModel Single();
        TModel SingleOrDefault();
        PagingSpec<TModel> GetPaged(PagingSpec<TModel> pagingSpec);
    }

    public class BasicRepository<TModel> : IBasicRepository<TModel>
        where TModel : ModelBase, new()
    {
        private static readonly ILogger Logger = NzbDroneLogger.GetLogger(typeof(BasicRepository<TModel>));

        private readonly IEventAggregator _eventAggregator;
        private readonly PropertyInfo _keyProperty;
        private readonly List<PropertyInfo> _properties;
        private readonly string _updateSql;
        private readonly string _insertSql;

        // Visibility: `protected` so ProviderRepository<T> (which overrides Query) can wrap
        // its own connection/reader loop in the same retry policy. Storage form: a `static
        // readonly` field rather than an expression-bodied property so the pipeline (and its
        // jitter RNG state) is built ONCE per AppDomain — Polly explicitly documents
        // ResiliencePipeline as reusable, thread-safe, and designed to be shared (WR-06).
        protected static readonly ResiliencePipeline RetryStrategy = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<SQLiteException>(ex => ex.ResultCode == SQLiteErrorCode.Busy),
                Delay = TimeSpan.FromMilliseconds(100),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    Logger.Warn(args.Outcome.Exception, "Database operation contended; retrying (attempt #{0})", args.AttemptNumber);

                    return default;
                }
            })
            .Build();

        protected readonly IDatabase _database;
        protected readonly string _table;

        public BasicRepository(IDatabase database, IEventAggregator eventAggregator)
        {
            _database = database;
            _eventAggregator = eventAggregator;

            var type = typeof(TModel);

            _table = TableMapping.Mapper.TableNameMapping(type);
            _keyProperty = type.GetProperty(nameof(ModelBase.Id));

            var excluded = TableMapping.Mapper.ExcludeProperties(type).Select(x => x.Name).ToList();
            excluded.Add(_keyProperty.Name);
            _properties = type.GetProperties().Where(x => x.IsMappableProperty() && !excluded.Contains(x.Name)).ToList();

            _insertSql = GetInsertSql();
            _updateSql = GetUpdateSql(_properties);
        }

        protected virtual SqlBuilder Builder() => new SqlBuilder(_database.DatabaseType);

        // Read-path wrap (Plan 01-05 Task 2): retry on SQLITE_BUSY. This funnel covers
        // Find / Get(int) / Get(IEnumerable<int>) / All / GetPaged transitively because
        // they all flow through Query(SqlBuilder). Postgres callers are unaffected -
        // the predicate filters SQLiteException only.
        protected virtual List<TModel> Query(SqlBuilder builder) =>
            RetryStrategy.Execute(
                static (state, _) => state.self._database.Query<TModel>(state.builder).ToList(),
                (self: this, builder));

        protected virtual List<TModel> QueryDistinct(SqlBuilder builder) =>
            RetryStrategy.Execute(
                static (state, _) => state.self._database.QueryDistinct<TModel>(state.builder).ToList(),
                (self: this, builder));

        protected List<TModel> Query(Expression<Func<TModel, bool>> where) => Query(Builder().Where(where));

        public int Count()
        {
            // Read-path wrap (Plan 01-05 Task 2): retry on SQLITE_BUSY.
            return RetryStrategy.Execute(
                static (state, _) =>
                {
                    using (var conn = state.self._database.OpenConnection())
                    {
                        return conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{state.table}\"");
                    }
                },
                (self: this, table: _table));
        }

        public virtual IEnumerable<TModel> All()
        {
            return Query(Builder());
        }

        public TModel Find(int id)
        {
            var model = Query(x => x.Id == id).FirstOrDefault();

            return model;
        }

        public TModel Get(int id)
        {
            var model = Find(id);

            if (model == null)
            {
                throw new ModelNotFoundException(typeof(TModel), id);
            }

            return model;
        }

        public IEnumerable<TModel> Get(IEnumerable<int> ids)
        {
            if (!ids.Any())
            {
                return Array.Empty<TModel>();
            }

            var result = Query(x => ids.Contains(x.Id));

            if (result.Count != ids.Count())
            {
                throw new ApplicationException($"Expected query to return {ids.Count()} rows but returned {result.Count}");
            }

            return result;
        }

        public TModel SingleOrDefault()
        {
            return All().SingleOrDefault();
        }

        public TModel Single()
        {
            return All().Single();
        }

        public TModel Insert(TModel model)
        {
            if (model.Id != 0)
            {
                throw new InvalidOperationException("Can't insert model with existing ID " + model.Id);
            }

            using (var conn = _database.OpenConnection())
            {
                model = Insert(conn, null, model);
            }

            ModelCreated(model);

            return model;
        }

        private string GetInsertSql()
        {
            var sbColumnList = new StringBuilder(null);
            for (var i = 0; i < _properties.Count; i++)
            {
                var property = _properties[i];
                sbColumnList.AppendFormat("\"{0}\"", property.Name);
                if (i < _properties.Count - 1)
                {
                    sbColumnList.Append(", ");
                }
            }

            var sbParameterList = new StringBuilder(null);
            for (var i = 0; i < _properties.Count; i++)
            {
                var property = _properties[i];
                sbParameterList.AppendFormat("@{0}", property.Name);
                if (i < _properties.Count - 1)
                {
                    sbParameterList.Append(", ");
                }
            }

            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                return $"INSERT INTO \"{_table}\" ({sbColumnList.ToString()}) VALUES ({sbParameterList.ToString()}) RETURNING \"Id\"";
            }

            return $"INSERT INTO {_table} ({sbColumnList.ToString()}) VALUES ({sbParameterList.ToString()}); SELECT last_insert_rowid() id";
        }

        private TModel Insert(IDbConnection connection, IDbTransaction transaction, TModel model)
        {
            SqlBuilderExtensions.LogQuery(_insertSql, model);

            var multi = RetryStrategy.Execute(static (state, _) => state.connection.QueryMultiple(state._insertSql, state.model, state.transaction), (connection, _insertSql, model, transaction));

            var multiRead = multi.Read();
            var id = (int)(multiRead.First().id ?? multiRead.First().Id);
            _keyProperty.SetValue(model, id);

            return model;
        }

        public void InsertMany(IList<TModel> models)
        {
            if (models.Any(x => x.Id != 0))
            {
                throw new InvalidOperationException("Can't insert model with existing ID != 0");
            }

            using (var conn = _database.OpenConnection())
            {
                using (var tran = conn.BeginTransaction(IsolationLevel.ReadCommitted))
                {
                    foreach (var model in models)
                    {
                        Insert(conn, tran, model);
                    }

                    tran.Commit();
                }
            }
        }

        public TModel Update(TModel model)
        {
            if (model.Id == 0)
            {
                throw new InvalidOperationException("Can't update model with ID 0");
            }

            using (var conn = _database.OpenConnection())
            {
                UpdateFields(conn, null, model, _properties);
            }

            ModelUpdated(model);

            return model;
        }

        public void UpdateMany(IList<TModel> models)
        {
            if (models.Any(x => x.Id == 0))
            {
                throw new InvalidOperationException("Can't update model with ID 0");
            }

            using (var conn = _database.OpenConnection())
            using (var tran = conn.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                UpdateFields(conn, tran, models, _properties);
                tran.Commit();
            }
        }

        protected void Delete(Expression<Func<TModel, bool>> where)
        {
            Delete(Builder().Where<TModel>(where));
        }

        protected void Delete(SqlBuilder builder)
        {
            var sql = builder.AddDeleteTemplate(typeof(TModel));

            // Delete-path wrap (Plan 01-05 Task 2): retry on SQLITE_BUSY. Funnels for
            // Delete(int), Delete(TModel), DeleteMany(IEnumerable<int>), DeleteMany(List<TModel>).
            RetryStrategy.Execute(
                static (state, _) =>
                {
                    using (var conn = state.self._database.OpenConnection())
                    {
                        return conn.Execute(state.sql.RawSql, state.sql.Parameters);
                    }
                },
                (self: this, sql));
        }

        public void Delete(TModel model)
        {
            Delete(model.Id);
        }

        public void Delete(int id)
        {
            Delete(x => x.Id == id);
        }

        public void DeleteMany(IEnumerable<int> ids)
        {
            if (ids.Any())
            {
                Delete(x => ids.Contains(x.Id));
            }
        }

        public void DeleteMany(List<TModel> models)
        {
            DeleteMany(models.Select(m => m.Id));
        }

        public TModel Upsert(TModel model)
        {
            // Upsert defensive wrap (Plan 01-05 Task 2): Insert / Update already retry,
            // but a top-level wrap is cheap insurance against future refactors that might
            // inline-SQL the upsert path (RESEARCH Open Question #2).
            return RetryStrategy.Execute(
                static (state, _) =>
                {
                    if (state.model.Id == 0)
                    {
                        state.self.Insert(state.model);
                        return state.model;
                    }

                    state.self.Update(state.model);
                    return state.model;
                },
                (self: this, model));
        }

        public void Purge(bool vacuum = false)
        {
            // Purge wrap (Plan 01-05 Task 2): retry on SQLITE_BUSY for the bulk DELETE.
            // Vacuum() is a maintenance op delegated to IDatabase and runs outside the
            // retry window (separate concurrency profile).
            RetryStrategy.Execute(
                static (state, _) =>
                {
                    using (var conn = state.self._database.OpenConnection())
                    {
                        return conn.Execute($"DELETE FROM \"{state.table}\"");
                    }
                },
                (self: this, table: _table));

            if (vacuum)
            {
                Vacuum();
            }
        }

        protected void Vacuum()
        {
            _database.Vacuum();
        }

        public bool HasItems()
        {
            return Count() > 0;
        }

        public void SetFields(TModel model, params Expression<Func<TModel, object>>[] properties)
        {
            if (model.Id == 0)
            {
                throw new InvalidOperationException("Attempted to update model without ID");
            }

            var propertiesToUpdate = properties.Select(x => x.GetMemberName()).ToList();

            using (var conn = _database.OpenConnection())
            {
                UpdateFields(conn, null, model, propertiesToUpdate);
            }

            ModelUpdated(model);
        }

        public void SetFields(IList<TModel> models, params Expression<Func<TModel, object>>[] properties)
        {
            if (models.Any(x => x.Id == 0))
            {
                throw new InvalidOperationException("Attempted to update model without ID");
            }

            var propertiesToUpdate = properties.Select(x => x.GetMemberName()).ToList();

            using (var conn = _database.OpenConnection())
            using (var tran = conn.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                UpdateFields(conn, tran, models, propertiesToUpdate);
                tran.Commit();
            }

            foreach (var model in models)
            {
                ModelUpdated(model);
            }
        }

        private string GetUpdateSql(List<PropertyInfo> propertiesToUpdate)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("UPDATE \"{0}\" SET ", _table);

            for (var i = 0; i < propertiesToUpdate.Count; i++)
            {
                var property = propertiesToUpdate[i];
                sb.AppendFormat("\"{0}\" = @{1}", property.Name, property.Name);
                if (i < propertiesToUpdate.Count - 1)
                {
                    sb.Append(", ");
                }
            }

            sb.Append($" WHERE \"{_keyProperty.Name}\" = @{_keyProperty.Name}");

            return sb.ToString();
        }

        private void UpdateFields(IDbConnection connection, IDbTransaction transaction, TModel model, List<PropertyInfo> propertiesToUpdate)
        {
            var sql = propertiesToUpdate == _properties ? _updateSql : GetUpdateSql(propertiesToUpdate);

            SqlBuilderExtensions.LogQuery(sql, model);

            RetryStrategy.Execute(static (state, _) => state.connection.Execute(state.sql, state.model, transaction: state.transaction), (connection, sql, model, transaction));
        }

        private void UpdateFields(IDbConnection connection, IDbTransaction transaction, IList<TModel> models, List<PropertyInfo> propertiesToUpdate)
        {
            var sql = propertiesToUpdate == _properties ? _updateSql : GetUpdateSql(propertiesToUpdate);

            foreach (var model in models)
            {
                SqlBuilderExtensions.LogQuery(sql, model);
            }

            RetryStrategy.Execute(static (state, _) => state.connection.Execute(state.sql, state.models, transaction: state.transaction), (connection, sql, models, transaction));
        }

        protected virtual SqlBuilder PagedBuilder() => Builder();
        protected virtual IEnumerable<TModel> PagedQuery(SqlBuilder sql) => Query(sql);

        public virtual PagingSpec<TModel> GetPaged(PagingSpec<TModel> pagingSpec)
        {
            pagingSpec.Records = GetPagedRecords(PagedBuilder(), pagingSpec, PagedQuery);
            pagingSpec.TotalRecords = GetPagedRecordCount(PagedBuilder().SelectCount(), pagingSpec);

            return pagingSpec;
        }

        protected void AddFilters(SqlBuilder builder, PagingSpec<TModel> pagingSpec)
        {
            var filters = pagingSpec.FilterExpressions;

            foreach (var filter in filters)
            {
                builder.Where<TModel>(filter);
            }
        }

        protected List<TModel> GetPagedRecords(SqlBuilder builder, PagingSpec<TModel> pagingSpec, Func<SqlBuilder, IEnumerable<TModel>> queryFunc)
        {
            AddFilters(builder, pagingSpec);

            if (pagingSpec.SortKey == null)
            {
                pagingSpec.SortKey = $"{_table}.{_keyProperty.Name}";
            }

            var sortKey = TableMapping.Mapper.GetSortKey(pagingSpec.SortKey);
            var sortDirection = pagingSpec.SortDirection == SortDirection.Descending ? "DESC" : "ASC";
            var pagingOffset = Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize;
            builder.OrderBy($"\"{sortKey.Table ?? _table}\".\"{sortKey.Column}\" {sortDirection} LIMIT {pagingSpec.PageSize} OFFSET {pagingOffset}");

            return queryFunc(builder).ToList();
        }

        protected int GetPagedRecordCount(SqlBuilder builder, PagingSpec<TModel> pagingSpec, string template = null)
        {
            AddFilters(builder, pagingSpec);

            SqlBuilder.Template sql;
            if (template != null)
            {
                sql = builder.AddTemplate(template).LogQuery();
            }
            else
            {
                sql = builder.AddPageCountTemplate(typeof(TModel));
            }

            // Read-path wrap (Plan 01-05 Task 2): retry on SQLITE_BUSY for paged-count.
            return RetryStrategy.Execute(
                static (state, _) =>
                {
                    using (var conn = state.self._database.OpenConnection())
                    {
                        return conn.ExecuteScalar<int>(state.sql.RawSql, state.sql.Parameters);
                    }
                },
                (self: this, sql));
        }

        protected void ModelCreated(TModel model, bool forcePublish = false)
        {
            PublishModelEvent(model, ModelAction.Created, forcePublish);
        }

        protected void ModelUpdated(TModel model, bool forcePublish = false)
        {
            PublishModelEvent(model, ModelAction.Updated, forcePublish);
        }

        protected void ModelDeleted(TModel model, bool forcePublish = false)
        {
            PublishModelEvent(model, ModelAction.Deleted, forcePublish);
        }

        private void PublishModelEvent(TModel model, ModelAction action, bool forcePublish)
        {
            if (PublishModelEvents || forcePublish)
            {
                _eventAggregator.PublishEvent(new ModelEvent<TModel>(model, action));
            }
        }

        protected virtual bool PublishModelEvents => false;
    }
}
