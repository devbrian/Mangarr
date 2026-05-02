using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Manga;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Abstract base for the <see cref="IMetadataSource"/> ThingiProvider family per D-14.
    /// Implements the seven IProvider members so concrete providers (MangaDex / AniList /
    /// MAL — Plans 02-06..02-08) only override Settings + the four search/info methods +
    /// <see cref="Test"/>.
    ///
    /// <see cref="DefaultIsPrimary"/> controls the IsPrimary value of the auto-created
    /// DefaultDefinition at first startup. MangaDex returns true (D-16 — default primary);
    /// the other two return false. Users can promote any of them via
    /// <c>MetadataSourceFactory.SetPrimary</c>.
    /// </summary>
    public abstract class MetadataSourceBase<TSettings> : IMetadataSource
        where TSettings : IProviderConfig, new()
    {
        protected readonly Logger _logger;

        protected MetadataSourceBase(Logger logger)
        {
            _logger = logger;
        }

        public abstract string Name { get; }

        /// <summary>
        /// Logical source name used as the rate-limit bucket. e.g. "mangadex", "anilist",
        /// "myanimelist". Mirrors <c>HttpAggregatorBase.DefaultSourceKey</c> shape per
        /// RESEARCH §Open Question 1 (sibling, not subclass).
        /// </summary>
        public abstract string DefaultSourceKey { get; }

        /// <summary>
        /// Initial value for the auto-created DefaultDefinition's IsPrimary flag (D-16).
        /// MangaDex=true; AniList/MAL=false. Users override via SetPrimary.
        /// </summary>
        public abstract bool DefaultIsPrimary { get; }

        public Type ConfigContract => typeof(TSettings);

        public virtual ProviderMessage Message => null;

        public virtual IEnumerable<ProviderDefinition> DefaultDefinitions => new[]
        {
            new MetadataSourceDefinition
            {
                Name = Name,
                ConfigContract = ConfigContract.Name,
                Implementation = GetType().Name,
                Settings = new TSettings(),
                IsPrimary = DefaultIsPrimary,
            }
        };

        public ProviderDefinition Definition { get; set; }

        public TSettings Settings => (TSettings)Definition.Settings;

        // --- IProvideMangaInfo split contract ---

        public abstract Tuple<Manga.Manga, List<Chapter>> GetMangaInfo(string sourceId);

        // --- ISearchForNewManga split contract ---

        public abstract List<Manga.Manga> SearchForNewManga(string title);

        public abstract List<Manga.Manga> SearchForNewMangaByMangaDexId(string mangaDexId);

        public abstract List<Manga.Manga> SearchForNewMangaByAniListId(int aniListId);

        public abstract List<Manga.Manga> SearchForNewMangaByMalId(int malId);

        // --- IProvider Test + RequestAction ---

        public abstract ValidationResult Test();

        public virtual object RequestAction(string stage, IDictionary<string, string> query)
        {
            return null;
        }

        public override string ToString()
        {
            return Definition?.Name ?? Name;
        }
    }
}
