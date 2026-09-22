// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Services;
using CourseMoth.Data;
using CourseMoth.Pages;
using CourseMoth.Services;
using CourseMoth.ViewModels;
using Microsoft.Extensions.Logging;

namespace CourseMoth
{
    public static class MauiProgramExtensions
    {
        public static MauiAppBuilder UseSharedMauiApp(this MauiAppBuilder builder)
        {
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            builder.Services.AddCourseMothDomain();
            builder.Services.AddCourseMothUi();

            return builder;
        }

        /// <summary>
        /// Registers the services each platform head has to supply itself.
        ///
        /// This project targets <c>net10.0</c>, so it cannot reach a platform API — and every
        /// folder picker is one. The head calls this after <see cref="UseSharedMauiApp"/> and
        /// supplies its own <see cref="IFolderPicker"/>.
        ///
        /// The two-step is not ceremony. Registering a stand-in here that merely compiles would
        /// put a dialog that cannot do the job behind a button that looks like it works, which is
        /// exactly the bug this replaced: "Add folder with courses" opened a <i>file</i> picker
        /// and discarded the choice.
        /// </summary>
        /// <param name="configure">Supplies the platform implementations.</param>
        public static MauiAppBuilder UsePlatformServices(
            this MauiAppBuilder builder,
            Action<IServiceCollection> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            configure(builder.Services);

            return builder;
        }

        /// <summary>
        /// The UI layer: pages, ViewModels and the app-shell concerns the ViewModels need.
        ///
        /// Pages and ViewModels are <b>transient</b>. A ViewModel is registered transient so that
        /// it starts each tab with a clean slate, and a page is registered transient because a
        /// singleton page cannot be re-attached to the visual tree once it has been removed from
        /// it. <see cref="AppShell"/> is the exception: exactly one shell is the root of the app.
        /// </summary>
        private static IServiceCollection AddCourseMothUi(this IServiceCollection services)
        {
            services.AddSingleton<AppShell>();

            // Shell-level concerns the ViewModels consume. The app layer is thin: these exist so
            // that no page or ViewModel reaches for the file system or a platform dialog itself.
            services.AddSingleton<IThemeService, ThemeService>();

            // IFolderPicker is registered by the platform head, not here — it has no
            // implementation in a project that cannot see a platform. See UsePlatformServices.

            // Home
            services.AddTransient<HomeViewModel>();
            services.AddTransient<HomePage>();

            // Library
            services.AddTransient<LibraryViewModel>();
            services.AddTransient<LibraryPage>();

            // Tasks
            services.AddTransient<TasksViewModel>();
            services.AddTransient<TasksPage>();

            // Downloads
            services.AddTransient<DownloadsViewModel>();
            services.AddTransient<DownloadsPage>();

            // Settings
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsPage>();

            return services;
        }

        /// <summary>
        /// Registers the domain services from <c>CourseMoth.Core</c>.
        ///
        /// Lives here rather than in the MAUI project proper so that there is exactly one place
        /// where the UI says "these are the domain services I depend on" — see SystemMap §3
        /// ("UI / App"): the UI calls services, it does not contain rules.
        ///
        /// Everything is registered against its Core interface. No implementation is constructed
        /// with <c>new</c> anywhere in the UI, and the UI names no concrete Core type outside this
        /// method (SystemMap §6).
        ///
        /// Lifetimes are singleton because every one of these is stateless: the state they work on
        /// lives in the repositories, and the day-boundary options are a value. <see cref="IClock"/>
        /// takes an explicit <see cref="TimeProvider"/> rather than reading the system clock
        /// directly, so a test host can place the app at 01:30 without touching the machine clock.
        /// </summary>
        private static IServiceCollection AddCourseMothDomain(this IServiceCollection services)
        {
            // The storage layer first: every service below takes a repository, so the
            // repositories have to be in the container before anything that needs them.
            //
            // The path is resolved here and passed down, because CourseMoth.Data deliberately has
            // no MAUI reference and therefore cannot ask FileSystem for it (StorageSpec §2:
            // "{AppDataDirectory}/coursemoth.db"). This is the only line in the app that knows
            // where the database lives.
            services.AddCourseMothData(Path.Combine(FileSystem.AppDataDirectory, "coursemoth.db"));

            // Time, and the study-day rule that depends on it (TasksStreaksSpec §3).
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<IStableKeyGenerator, StableKeyGenerator>();
            services.AddSingleton<DayBoundaryOptions>();

            // Study-date arithmetic over the activity log. A concrete type, not an interface —
            // it is a helper the services below hold directly, and nothing substitutes it.
            services.AddSingleton<ActivityTracker>();

            // Core services, in dependency order. Everything here is a singleton because every one
            // of them is stateless: the state they work on lives in the repositories, and the
            // options and the clock are values. The repositories themselves are singletons for the
            // reason recorded in CourseMoth.Data: one shared SQLite connection per process
            // (ServerApiSpec §2, "Storage note" — there must not be a second writer).
            services.AddSingleton<ICourseFingerprintService, CourseFingerprintService>();
            services.AddSingleton<IProgressCalculator, ProgressCalculator>();
            services.AddSingleton<IStreakCalculator, StreakCalculator>();
            services.AddSingleton<ITaskEvaluator, TaskEvaluator>();
            services.AddSingleton<IWatchStateService, WatchStateService>();
            services.AddSingleton<IImportService, ImportService>();

            // TODO: IImportService resolves, but nothing can call it yet.
            //
            //   ImportService.ImportAsync takes a ParsedRoot, and there is no CourseMoth.Parser
            //   project — the parser is stage 2 and has not been written. The registration above
            //   is honest: the service is constructible and the graph is complete, but the UI has
            //   no way to produce its input. The "Add folder" action on Home therefore still stops
            //   at IFolderPicker (see FolderPicker's own remarks).
            //
            //   Also unregistered, deliberately, because Core declares no interface for them and
            //   inventing one here would be a contract Core has not asked for:
            //     - CourseFingerprint / SyncEntityMetadata persistence — SyncSpec, a later stage.
            //     - DownloadJob — stage 7; the DownloadsViewModel is a placeholder by design and
            //       injects nothing, so there is nothing for a repository to serve yet.
            //   Data's schema already carries the sync table and the fingerprint entity is stored
            //   per-course on import, so neither needs a migration when its stage arrives.

            return services;
        }
    }
}
