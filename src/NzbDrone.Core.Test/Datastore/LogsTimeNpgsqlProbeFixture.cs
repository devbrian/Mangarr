using System;
using System.Data;
using FluentAssertions;
using Npgsql;
using NUnit.Framework;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Test.Datastore
{
    // Issue #270 / PR #275 (Codex P1) regression guard. Logs.Time is written by NLog's
    // DatabaseTarget.WritePostgresLog via a raw `NpgsqlParameter("Time", DbType.DateTime)`
    // carrying a Kind=Utc value (DatabaseTarget.cs:122) — NOT the Dapper UtcConverter path.
    //
    // Migration 008 alters Logs.Time from `timestamp without time zone` to `timestamptz`.
    // This test locks in the Npgsql behavior that makes that the correct (and necessary)
    // fix: under a NON-UTC session timezone the raw write path round-trips a UTC instant
    // bit-for-bit through a `timestamptz` column, but a `timestamp without time zone` column
    // applies a session-TZ offset shift. Postgres-only; skipped on SQLite.
    [TestFixture]
    public class LogsTimeNpgsqlProbeFixture
    {
        private string _connectionString;

        [SetUp]
        public void SetUp()
        {
            var options = PostgresOptions.GetOptions();
            if (string.IsNullOrWhiteSpace(options.Host))
            {
                Assert.Ignore("Postgres env not configured — Logs.Time round-trip guard is Postgres-only.");
            }

            _connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = options.Host,
                Port = options.Port,
                Username = options.User,
                Password = options.Password,
                Database = "postgres",
            }.ConnectionString;
        }

        [Test]
        public void timestamptz_preserves_utc_instant_under_non_utc_session_but_tz_less_shifts()
        {
            var when = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            // The condition that exposes the defect: a non-UTC session timezone.
            Exec(conn, "SET TIME ZONE 'America/New_York'");
            Exec(conn,
                "DROP TABLE IF EXISTS logs_time_guard; " +
                "CREATE TEMP TABLE logs_time_guard (t_plain timestamp, t_tz timestamptz)");

            // Exact DatabaseTarget.WritePostgresLog write shape: DbType.DateTime + Kind=Utc value.
            // It writes cleanly to BOTH column types — only the round-trip fidelity differs.
            using (var ins = conn.CreateCommand())
            {
                ins.CommandText = "INSERT INTO logs_time_guard (t_plain, t_tz) VALUES (@a, @b)";
                ins.Parameters.Add(new NpgsqlParameter("a", DbType.DateTime) { Value = when });
                ins.Parameters.Add(new NpgsqlParameter("b", DbType.DateTime) { Value = when });
                ins.ExecuteNonQuery();
            }

            string plainAsUtc, tzAsUtc;
            using (var sel = conn.CreateCommand())
            {
                sel.CommandText =
                    "SELECT to_char(t_plain AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS'), " +
                    "       to_char(t_tz    AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS') " +
                    "FROM logs_time_guard";
                using var r = sel.ExecuteReader();
                r.Read();
                plainAsUtc = r.GetString(0);
                tzAsUtc = r.GetString(1);
            }

            // timestamptz (the post-Migration-008 Logs.Time type) preserves the instant.
            tzAsUtc.Should().Be("2026-01-15T12:00:00",
                "Migration 008 alters Logs.Time to timestamptz precisely so the UTC instant survives a non-UTC session");

            // tz-less (the pre-008 type) shifts it — this is the defect the migration fixes.
            plainAsUtc.Should().NotBe("2026-01-15T12:00:00",
                "a `timestamp without time zone` column applies a session-TZ offset shift — why Logs.Time must NOT stay tz-less");
        }

        private static void Exec(NpgsqlConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
