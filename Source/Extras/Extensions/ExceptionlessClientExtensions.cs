﻿using System;
using System.Threading.Tasks;
using Exceptionless.Dependency;
using Exceptionless.Plugins;
using Exceptionless.Extras.Extensions;
using Exceptionless.Extras.Submission;
using Exceptionless.Logging;
using Exceptionless.Services;
using Exceptionless.Submission;

namespace Exceptionless {
    public static class ExceptionlessClientExtensions {
        /// <summary>
        /// Reads configuration settings, configures various plugins and wires up to platform specific exception handlers. 
        /// </summary>
        /// <param name="client">The ExceptionlessClient.</param>
        /// <param name="appDomain">The AppDomain to register the exception handlers on.</param>
        public static void Startup(this ExceptionlessClient client, AppDomain appDomain = null) {
            if (client.Configuration.Resolver.HasDefaultRegistration<ISubmissionClient, DefaultSubmissionClient>())
                client.Configuration.Resolver.Register<ISubmissionClient, SubmissionClient>();

            if (client.Configuration.Resolver.HasDefaultRegistration<IEnvironmentInfoCollector, DefaultEnvironmentInfoCollector>())
                client.Configuration.Resolver.Register<IEnvironmentInfoCollector, EnvironmentInfoCollector>();

            client.Configuration.ReadAllConfig();
            client.Configuration.UseErrorPlugin();
            client.Configuration.UseTraceLogEntriesPlugin();
            client.Configuration.AddPlugin<VersionPlugin>();
            client.Configuration.AddPlugin<PrivateInformationPlugin>();
            
            client.RegisterAppDomainUnhandledExceptionHandler(appDomain);
            client.RegisterTaskSchedulerUnobservedTaskExceptionHandler();
        }

        /// <summary>
        /// Unregisters platform specific exception handlers.
        /// </summary>
        /// <param name="client">The ExceptionlessClient.</param>
        /// <param name="appDomain">The AppDomain to unregister any exception handlers from.</param>
        public static void Shutdown(this ExceptionlessClient client, AppDomain appDomain = null) {
            client.UnregisterAppDomainUnhandledExceptionHandler(appDomain);
            client.UnregisterTaskSchedulerUnobservedTaskExceptionHandler();
        }
    }
}

namespace Exceptionless.Extras.Extensions {
    public static class ExceptionlessClientExtensions {
        private static UnhandledExceptionEventHandler _onAppDomainUnhandledException;
        public static void RegisterAppDomainUnhandledExceptionHandler(this ExceptionlessClient client, AppDomain appDomain = null) {
            if (appDomain == null)
                appDomain = AppDomain.CurrentDomain;

            if (_onAppDomainUnhandledException == null)
                _onAppDomainUnhandledException = (sender, args) => {
                    var exception = args.ExceptionObject as Exception;
                    if (exception == null)
                        return;

                    var contextData = new ContextData();
                    contextData.MarkAsUnhandledError();
                    contextData.SetSubmissionMethod("AppDomainUnhandledException");

                    exception.ToExceptionless(contextData, client).Submit();

                    // process queue immediately since the app is about to exit.
                    client.ProcessQueue();
                };

            try {
                appDomain.UnhandledException -= _onAppDomainUnhandledException;
                appDomain.UnhandledException += _onAppDomainUnhandledException;
            } catch (Exception ex) {
                client.Configuration.Resolver.GetLog().Error(typeof(ExceptionlessClientExtensions), ex, "An error occurred while wiring up to the unhandled exception event. This will happen when you are not running under full trust.");
            }
        }

        public static void UnregisterAppDomainUnhandledExceptionHandler(this ExceptionlessClient client, AppDomain appDomain = null) {
            if (_onAppDomainUnhandledException == null)
                return;

            if (appDomain == null)
                appDomain = AppDomain.CurrentDomain;

            appDomain.UnhandledException -= _onAppDomainUnhandledException;
            _onAppDomainUnhandledException = null;
        }

        private static EventHandler<UnobservedTaskExceptionEventArgs> _onTaskSchedulerOnUnobservedTaskException;
        public static void RegisterTaskSchedulerUnobservedTaskExceptionHandler(this ExceptionlessClient client) {
            if (_onTaskSchedulerOnUnobservedTaskException == null)
                _onTaskSchedulerOnUnobservedTaskException = (sender, args) => {
                    var contextData = new ContextData();
                    contextData.MarkAsUnhandledError();
                    contextData.SetSubmissionMethod("UnobservedTaskException");

                    args.Exception.ToExceptionless(contextData, client).Submit();
                };

            try {
                TaskScheduler.UnobservedTaskException -= _onTaskSchedulerOnUnobservedTaskException;
                TaskScheduler.UnobservedTaskException += _onTaskSchedulerOnUnobservedTaskException;
            } catch (Exception ex) {
                client.Configuration.Resolver.GetLog().Error(typeof(ExceptionlessClientExtensions), ex, "An error occurred while wiring up to the unobserved task exception event.");
            }
        }

        public static void UnregisterTaskSchedulerUnobservedTaskExceptionHandler(this ExceptionlessClient client) {
            if (_onTaskSchedulerOnUnobservedTaskException == null)
                return;

            TaskScheduler.UnobservedTaskException -= _onTaskSchedulerOnUnobservedTaskException;
            _onTaskSchedulerOnUnobservedTaskException = null;
        }
    }
}