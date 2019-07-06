using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using NLog;
using NLog.Config;
using NLog.Targets;
using VAMLaunch;

namespace VaMLaunchGUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly NLog.Logger _log;
        private VAMLaunchServer server;
        private Task _serverTask;
        private bool _positionReceived = false;

        public MainWindow()
        {
            InitializeComponent();
            if (Application.Current == null)
            {
                return;
            }


            _log = LogManager.GetCurrentClassLogger();
            LogManager.Configuration = LogManager.Configuration ?? new LoggingConfiguration();
#if DEBUG
            // Debug Logger Setup
            var t = new DebuggerTarget();
            LogManager.Configuration.AddTarget("debugger", t);
            LogManager.Configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Debug, t));
            LogManager.Configuration = LogManager.Configuration;
#endif

            _log.Info("Application started.");
            server = new VAMLaunchServer();
            _serverTask = new Task (() => server.UpdateThread());
            _serverTask.Start();
            // This is an event handler that will be executed on an outside thread, so remember to use dispatcher.
            server.PositionUpdate += OnPositionUpdate;
        }

        protected void OnPositionUpdate(object aObj, PositionUpdateEventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                if (!_positionReceived)
                {
                    ConnectionStatus.Content = "Connected to VaM";
                }

                await _intifaceTab.Linear((uint)(e.Duration * 1000), (double)(e.Position / 100.0));
            });
        }
    }
}
