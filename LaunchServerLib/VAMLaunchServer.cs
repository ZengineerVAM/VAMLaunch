using System;
using System.Threading;

namespace VAMLaunch
{
    public class PositionUpdateEventArgs : EventArgs
    {
        // 0-99, in Launch FW 1.2 message units
        public uint Position;
        // 0-99, in Launch FW 1.2 message units
        public uint Speed;
        // time in decimal seconds
        public float Duration;
    }


    public class VAMLaunchServer
    {
        public EventHandler<PositionUpdateEventArgs> PositionUpdate;
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

        public void UpdateThread()
        {
            _network = new VAMLaunchNetwork();
            if (!_network.Init(SERVER_IP, SERVER_LISTEN_PORT, SERVER_SEND_PORT))
            {
                return;
            }

            _running = true;

            Console.WriteLine("SERVER IS ON");
            Console.WriteLine("Your Launch device is ready to go when your device shows a solid blue light.");
            Console.WriteLine("Note: You can type the command \"lc\" to attempt to re-establish communication.");

            _timeOfLastLaunchUpdate = DateTime.Now;
            
            while (_running)
            {
                lock (_inputLock)
                {
                    _userCmd = null;
                }

                ProcessNetworkMessages();
                UpdateMovement();

                if (_running)
                {
                    Thread.Sleep(1000 / NETWORK_POLL_RATE);
                }
            }

            _network.Stop();
        }

        private void UpdateMovement()
        {
            var now = DateTime.Now;
            TimeSpan timeSinceLastUpdate = now - _timeOfLastLaunchUpdate;
            if (timeSinceLastUpdate.TotalSeconds > LAUNCH_UPDATE_INTERVAL)
            {
                if (_hasNewLaunchSnapshot)
                {
                    PositionUpdate?.Invoke(this, new PositionUpdateEventArgs { Position = _latestLaunchPos, Speed = _latestLaunchSpeed, Duration = _latestLaunchDuration });
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
