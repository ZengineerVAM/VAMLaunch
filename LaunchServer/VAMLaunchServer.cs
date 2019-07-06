using System;
using System.Threading;
using Buttplug.Client;
using Buttplug.Core.Logging;
using Buttplug.Core.Messages;
using Buttplug.Server;
using DeviceAddedEventArgs = Buttplug.Client.DeviceAddedEventArgs;

namespace VAMLaunch
{
    public class VAMLaunchServer
    {
        private const string SERVER_IP = "127.0.0.1";
        private const int SERVER_LISTEN_PORT = 15601;
        private const int SERVER_SEND_PORT = 15600;
        private const int NETWORK_POLL_RATE = 60;
        private const int LAUNCH_UPDATE_RATE = 60;
        private const float LAUNCH_UPDATE_INTERVAL = 1.0f / LAUNCH_UPDATE_RATE;
        
        private VAMLaunchNetwork _network;
        private Thread _updateThread;

        private object _inputLock = new object();
        private string _userCmd;

        private bool _running;

        private byte _latestLaunchPos;
        private byte _latestLaunchSpeed;
        private float _latestLaunchDuration;
        private bool _hasNewLaunchSnapshot;
        private DateTime _timeOfLastLaunchUpdate;

        private ButtplugClient _client;
        private DeviceManager _deviceManager;
        private ButtplugClientDevice _launchDevice;

        public void Run()
        {
            _updateThread = new Thread(UpdateThread);
            _updateThread.Start();

            _running = true;
            
            while (_updateThread.IsAlive)
            {
                if (_running)
                {
                    string input = Console.ReadLine();

                    lock (_inputLock)
                    {
                        _userCmd = input;
                    }
                }
            }
        }

        private void UpdateThread()
        {
            _network = new VAMLaunchNetwork();
            if (!_network.Init(SERVER_IP, SERVER_LISTEN_PORT, SERVER_SEND_PORT))
            {
                return;
            }
            
            Console.WriteLine("SERVER IS ON");
            Console.WriteLine("Your Launch device is ready to go when your device shows a solid blue light.");
            Console.WriteLine("Note: You can type the command \"lc\" to attempt to re-establish communication.");

            _timeOfLastLaunchUpdate = DateTime.Now;
            
            TryConnectToLaunch();
            
            while (_running)
            {
                lock (_inputLock)
                {
                    ProcessUserCmd(_userCmd);
                    _userCmd = null;
                }

                ProcessNetworkMessages();
                UpdateLaunch();

                if (_running)
                {
                    Thread.Sleep(1000 / NETWORK_POLL_RATE);
                }
            }
            
            // While this requires a wait as this is normally an async call,
            // that's usually to confirm network shutdown. We're fine to just
            // block on this until all devices are stopped.
            _client.DisconnectAsync().Wait();
            
            _network.Stop();
        }

        private void OnDisconnected(object sender, EventArgs e)
        {
            _launchDevice = null;
            Console.WriteLine("**Launch Disconnected**");
        }

        private void OnDeviceRemoved(object sender, DeviceRemovedEventArgs e)
        {
            // Only update if we've dropped our linear movement device.
            if (_launchDevice != null && e.Device.Index == _launchDevice.Index)
            {
                _launchDevice = null;
                Console.WriteLine("**Launch Device Removed**");
            }
        }

        private void OnDeviceAdded(object sender, DeviceAddedEventArgs e)
        {
            if (_launchDevice != null)
            {
                Console.WriteLine($"**Device {e.Device.Name} Found but we already have a device, ignoring**");
                return;
            }
            if (!e.Device.AllowedMessages.ContainsKey(typeof(LinearCmd)))
            {
                Console.WriteLine($"**Device {e.Device.Name} Found but does not support linear movement, ignoring**");
                return;
            }
            Console.WriteLine($"**Adding Device {e.Device.Name}");
            _launchDevice = e.Device;
        }

        private void LaunchDeviceOnDisconnected(object sender, Exception e)
        {
            Console.WriteLine("**Launch Disconnected** {0}", e.Message);
        }

        private void OnLogMessage(object sender, LogEventArgs e)
        {
            Console.WriteLine($"Buttplug: {e.Message.LogLevel} {e.Message.LogMessage}");
        }

        private void TryConnectToLaunch()
        {
            _launchDevice = null;
            if (_client != null)
            {
                _client.DisconnectAsync().Wait();
            }
            
            _client = new ButtplugClient("VaMLaunch Client", new ButtplugEmbeddedConnector("VaMLaunch Server"));
            _client.DeviceAdded += OnDeviceAdded;
            _client.DeviceRemoved += OnDeviceRemoved;
            _client.ServerDisconnect += OnDisconnected;
            //_client.Log += OnLogMessage;
            _client.ConnectAsync().Wait();
            _client.RequestLogAsync(ButtplugLogLevel.Debug).Wait();
            _client.StartScanningAsync().Wait();
        }
        
        private void ProcessUserCmd(string cmd)
        {
            if (string.IsNullOrEmpty(cmd))
            {
                return;
            }

            var splits = cmd.Split(' ');
            if (splits.Length == 0)
            {
                return;
            }
            
            if (splits[0] == "exit")
            {
                _running = false;
            }
            else if (splits[0] == "lc")
            {
                TryConnectToLaunch();
            }
            else if (splits[0] == "pos")
            {
                if (splits.Length < 3)
                {
                    return;
                }

                byte pos;
                byte speed;

                if (byte.TryParse(splits[1], out pos) && byte.TryParse(splits[2], out speed))
                {
                    _latestLaunchPos = Math.Max(Math.Min(pos, (byte)99), (byte)0);
                    _latestLaunchSpeed = Math.Max(Math.Min(speed, (byte)99), (byte)0);
                }
            }
        }

        private async void SetLaunchPosition(byte pos, byte speed)
        {
            if (_launchDevice != null)
            {
                await _launchDevice.SendFleshlightLaunchFW12Cmd(speed, pos);
            }
        }

        private void UpdateLaunch()
        {
            if (_launchDevice == null)
            {
                return;
            }

            var now = DateTime.Now;
            TimeSpan timeSinceLastUpdate = now - _timeOfLastLaunchUpdate;
            if (timeSinceLastUpdate.TotalSeconds > LAUNCH_UPDATE_INTERVAL)
            {
                if (_hasNewLaunchSnapshot)
                {
                    SetLaunchPosition(_latestLaunchPos, _latestLaunchSpeed);
                    _hasNewLaunchSnapshot = false;
                }
                
                _timeOfLastLaunchUpdate = now;
            }
        }

        private void ProcessNetworkMessages()
        {
            byte[] msg = _network.GetNextMessage();
            if (msg != null && msg.Length == 6)
            {
                _latestLaunchPos = msg[0];
                _latestLaunchSpeed = msg[1];
                _latestLaunchDuration = BitConverter.ToSingle(msg, 2);

//                Console.WriteLine("Receiving: P:{0}, S:{1}, D:{2}", _latestLaunchPos, _latestLaunchSpeed,
//                    _latestLaunchDuration);
                
                _hasNewLaunchSnapshot = true;
            }
        }
    }
}
