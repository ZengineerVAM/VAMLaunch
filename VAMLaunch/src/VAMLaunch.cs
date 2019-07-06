using System;
using System.Collections.Generic;
using UnityEngine;
using VAMLaunchPlugin.MotionSources;

namespace VAMLaunchPlugin
{
    public class VAMLaunch : MVRScript
    {
        private static VAMLaunch _instance;
        
        private const string SERVER_IP = "127.0.0.1";
        private const int SERVER_LISTEN_PORT = 15600;
        private const int SERVER_SEND_PORT = 15601;
        private const float NETWORK_LISTEN_INTERVAL = 0.033f;
        
        private VAMLaunchNetwork _network;
        private float _networkPollTimer;

        private byte _lastSentLaunchPos;

        private JSONStorableStringChooser _motionSourceChooser;
        private JSONStorableBool _pauseLaunchMessages;
        private JSONStorableFloat _simulatorPosition;
        
        private float _simulatorTarget;
        private float _simulatorSpeed;

        private IMotionSource _currentMotionSource;
        private int _currentMotionSourceIndex = -1;
        private int _desiredMotionSourceIndex;

        private List<string> _motionSourceChoices = new List<string>
        {
            "Oscillate",
            "Pattern",
            "Zone"
        };

        private List<IMotionSource> _motionSources = new List<IMotionSource>
        {
            new OscillateSource(),
            new PatternSource(),
            new ZoneSource()
        };
        
        public override void Init()
        {
            if (_instance != null)
            {
                SuperController.LogError("You can only have one instance of VAM Launch active!");
                return;
            }
            
            if (containingAtom == null || containingAtom.type == "CoreControl")
            {
                SuperController.LogError("Please add VAM Launch to in scene atom!");
                return;
            }

            _instance = this;

            InitStorables();
            InitOptionsUI();
            InitActions();
            InitNetwork();
        }

        private void InitNetwork()
        {
            _network = new VAMLaunchNetwork();
            _network.Init(SERVER_IP, SERVER_LISTEN_PORT, SERVER_SEND_PORT);
            SuperController.LogMessage("VAM Launch network connection established.");
        }
        
        private void InitStorables()
        {
            _motionSourceChooser = new JSONStorableStringChooser("motionSource", _motionSourceChoices, "",
                "Motion Source",
                (string name) => { _desiredMotionSourceIndex = GetMotionSourceIndex(name); });
            _motionSourceChooser.choices = _motionSourceChoices;
            RegisterStringChooser(_motionSourceChooser);
            if (string.IsNullOrEmpty(_motionSourceChooser.val))
            {
                _motionSourceChooser.SetVal(_motionSourceChoices[0]);
            }
            
            _pauseLaunchMessages = new JSONStorableBool("pauseLaunchMessages", true);
            RegisterBool(_pauseLaunchMessages);
            
            _simulatorPosition = new JSONStorableFloat("simulatorPosition", 0.0f, 0.0f, LaunchUtils.LAUNCH_MAX_VAL);
            RegisterFloat(_simulatorPosition);

            foreach (var ms in _motionSources)
            {
                ms.OnInitStorables(this);
            }
        }
        
        private void InitOptionsUI()
        {
            var toggle = CreateToggle(_pauseLaunchMessages);
            toggle.label = "Pause Launch";
            
            var slider = CreateSlider(_simulatorPosition, false);
            slider.label = "Simulator";
            
            CreateScrollablePopup(_motionSourceChooser);

            CreateSpacer();
        }

        private void InitActions()
        {
            JSONStorableAction startLaunchAction = new JSONStorableAction("startLaunch", () =>
            {
                _pauseLaunchMessages.SetVal(false);
            });
            RegisterAction(startLaunchAction);
            
            JSONStorableAction stopLaunchAction = new JSONStorableAction("stopLaunch", () =>
            {
                _pauseLaunchMessages.SetVal(true);
            });
            RegisterAction(stopLaunchAction);
            
            JSONStorableAction toggleLaunchAction = new JSONStorableAction("toggleLaunch", () =>
            {
                _pauseLaunchMessages.SetVal(!_pauseLaunchMessages.val);
            });
            RegisterAction(toggleLaunchAction);
        }

        private int GetMotionSourceIndex(string name)
        {
            return _motionSourceChoices.IndexOf(name);
        }
        
        private void UpdateMotionSource()
        {
            if (_desiredMotionSourceIndex != _currentMotionSourceIndex)
            {
                if (_currentMotionSource != null)
                {
                    _currentMotionSource.OnDestroy(this);
                    _currentMotionSource = null;
                }

                if (_desiredMotionSourceIndex >= 0)
                {
                    _currentMotionSource = _motionSources[_desiredMotionSourceIndex];
                    _currentMotionSource.OnInit(this);
                }

                _currentMotionSourceIndex = _desiredMotionSourceIndex;
            }

            if (_currentMotionSource != null)
            {
                byte pos = 0;
                byte speed = 0;
                if (_currentMotionSource.OnUpdate(ref pos, ref speed))
                {
                    SendLaunchPosition(pos, speed);
                }
            }
        }
        
        private void OnDestroy()
        {
            if (_network != null)
            {
                SuperController.LogMessage("Shutting down VAM Launch network.");
                _network.Stop();
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            UpdateMotionSource();
            
            UpdateNetwork();
            UpdateSimulator();
        }

        private void UpdateSimulator()
        {
            var prevPos = _simulatorPosition.val;

            var newPos = Mathf.MoveTowards(prevPos, _simulatorTarget,
                LaunchUtils.PredictDistanceTraveled(_simulatorSpeed, Time.deltaTime));
            
            _simulatorPosition.SetVal(newPos);

            if (_currentMotionSource != null)
            {
                _currentMotionSource.OnSimulatorUpdate(prevPos, newPos, Time.deltaTime);
            }
        }

        private void SetSimulatorTarget(float pos, float speed)
        {
            _simulatorTarget = Mathf.Clamp(pos, 0.0f, LaunchUtils.LAUNCH_MAX_VAL);
            _simulatorSpeed = Mathf.Clamp(speed, 0.0f, LaunchUtils.LAUNCH_MAX_VAL);
        }
        
        // Not really used yet, but there just incase we want to do two way communication between server
        private void UpdateNetwork()
        {
            if (_network == null)
            {
                return;
            }

            _networkPollTimer -= Time.deltaTime;
            if (_networkPollTimer <= 0.0f)
            {
                ReceiveNetworkMessages();
                _networkPollTimer = NETWORK_LISTEN_INTERVAL - Mathf.Min(-_networkPollTimer, NETWORK_LISTEN_INTERVAL);
            }
        }

        private void ReceiveNetworkMessages()
        {
            byte[] msg = _network.GetNextMessage();
            if (msg != null && msg.Length > 0)
            {
                //SuperController.LogMessage(msg[0].ToString());
            }
        }

        
        private static byte[] _launchData = new byte[6];
        private void SendLaunchPosition(byte pos, byte speed)
        {
            SetSimulatorTarget(pos, speed);
            
            if (_network == null)
            {
                return;
            }

            if (!_pauseLaunchMessages.val)
            {
                _launchData[0] = pos;
                _launchData[1] = speed;

                float dist = Mathf.Abs(pos - _lastSentLaunchPos);
                float duration = LaunchUtils.PredictMoveDuration(dist, speed);
                    
                var durationData = BitConverter.GetBytes(duration);
                _launchData[2] = durationData[0];
                _launchData[3] = durationData[1];
                _launchData[4] = durationData[2];
                _launchData[5] = durationData[3];
                
                //SuperController.LogMessage(string.Format("Sending: P:{0}, S:{1}, D:{2}", pos, speed, duration));
                
                _network.Send(_launchData, _launchData.Length);

                _lastSentLaunchPos = pos;
            }
        }
    }
}