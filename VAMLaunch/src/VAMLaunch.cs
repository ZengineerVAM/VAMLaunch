using System.Collections.Generic;
using UnityEngine;

namespace VAMLaunchPlugin
{
    public class VAMLaunch : MVRScript
    {
        private static VAMLaunch _instance;
        
        private const string SERVER_IP = "127.0.0.1";
        private const int SERVER_LISTEN_PORT = 15600;
        private const int SERVER_SEND_PORT = 15601;
        private const float NETWORK_LISTEN_INTERVAL = 0.033f;

        private const float LAUNCH_MAX_VAL = 99.0f;
        private const float LAUNCH_LENGTHS_PER_SECOND = 6.0f;
        
        private const float ZONE_MESH_SCALAR = 20.0f;
        
        private VAMLaunchNetwork _network;
        private float _networkPollTimer;

        private FreeControllerV3 _controller;
        private LineDrawer _lineDrawer;
        private Material _zoneMaterial;

        private JSONStorableFloat _minLaunchSignalTimeThreshold;
        private JSONStorableFloat _maxLaunchSignalTimeThreshold;
        private JSONStorableFloat _currentLaunchSignalTimeThreshold;
        private JSONStorableFloat _lowerVelocityBarrier;
        private JSONStorableFloat _higherVelocityBarrier;
        private JSONStorableFloat _launchSpeedMultiplier;
        private JSONStorableBool _pauseLaunchMessages;
        private JSONStorableFloat _debugVelocity;
        
        private JSONStorableFloat _targetZoneWidth;
        private JSONStorableFloat _targetZoneHeight;
        private JSONStorableFloat _targetZoneDepth;

        private JSONStorableFloat _currentLaunchPos;
        private float _lastLaunchPos;

        private JSONStorableStringChooser _targetAtomChooser;
        private JSONStorableStringChooser _targetControllerChooser;
     
        private Atom _targetAtom;
        private FreeControllerV3 _targetController;
        
        private Matrix4x4 _zoneRenderMatrix;
        private Matrix4x4 _zoneInvMatrix;

        public const int VELOCITY_BUFFER_CAPACITY = 20;
        private Queue<float> _upwardsVelocityBuffer = new Queue<float>(VELOCITY_BUFFER_CAPACITY);
        private Queue<float> _downwardsVelocityBuffer = new Queue<float>(VELOCITY_BUFFER_CAPACITY);
        
        public const int AVG_VELOCITY_BUFFER_CAPACITY = 100;
        private List<float> _velocityHistory = new List<float>(AVG_VELOCITY_BUFFER_CAPACITY);
        
        private float _timeMovingUpwards;
        private float _timeMovingDownwards;
        
        private JSONStorableFloat _simulatorPosition;
        private float _simulatorTarget;
        private float _simulatorSpeed;
        
        public override void Init()
        {
            if (_instance != null)
            {
                SuperController.LogError("You can only have one instance of VAM Launch active!");
                return;
            }
            
            if (containingAtom == null || containingAtom.type == "CoreControl")
            {
                SuperController.LogError("Please add VAM Launch to an empty atom instead!");
                return;
            }

            _instance = this;

            InitController();
            InitValues();
            InitMenu();
            InitRenderers();
            
            InitNetwork();
        }

        private void InitController()
        {
            _controller = containingAtom.GetStorableByID("control") as FreeControllerV3;
        }

        private void InitRenderers()
        {
            _lineDrawer = new LineDrawer(_controller.linkLineMaterial);
            _zoneMaterial = new Material(_controller.linkLineMaterial);

            Color zoneColor = Color.magenta;
            zoneColor.a = 0.2f;
            _zoneMaterial.SetColor("_Color", zoneColor);
        }

        private void InitNetwork()
        {
            _network = new VAMLaunchNetwork();
            _network.Init(SERVER_IP, SERVER_LISTEN_PORT, SERVER_SEND_PORT);
            SuperController.LogMessage("VAM Launch network connection established.");
        }
        
        private void InitValues()
        {
            _targetZoneWidth = new JSONStorableFloat("targetZoneWidth", 0.1f, 0.005f, 0.2f);
            _targetZoneHeight = new JSONStorableFloat("targetZoneHeight", 0.1f, 0.005f, 0.2f);
            _targetZoneDepth = new JSONStorableFloat("targetZoneDepth", 0.1f, 0.005f, 0.2f);
            
            RegisterFloat(_targetZoneWidth);
            RegisterFloat(_targetZoneHeight);
            RegisterFloat(_targetZoneDepth);
            
            _currentLaunchPos = new JSONStorableFloat("launchPos", 0.0f, 0.0f, LAUNCH_MAX_VAL);
            RegisterFloat(_currentLaunchPos);
            
            _minLaunchSignalTimeThreshold = new JSONStorableFloat("minLaunchSignalTimeThreshold", 0.1f, 0.001f, 0.4f);
            RegisterFloat(_minLaunchSignalTimeThreshold);
            
            _maxLaunchSignalTimeThreshold = new JSONStorableFloat("maxLaunchSignalTimeThreshold", 0.4f, 0.001f, 0.4f);
            RegisterFloat(_maxLaunchSignalTimeThreshold);
            
            _currentLaunchSignalTimeThreshold = new JSONStorableFloat("currentLaunchSignalTimeThreshold", 0.099f, 0.001f, 0.4f);
            RegisterFloat(_currentLaunchSignalTimeThreshold);
            
            _lowerVelocityBarrier = new JSONStorableFloat("lowerVelocityBarrier", 10.0f, 0.0f, LAUNCH_MAX_VAL);
            RegisterFloat(_lowerVelocityBarrier);
            
            _higherVelocityBarrier = new JSONStorableFloat("higherVelocityBarrier", 40.0f, 0.0f, LAUNCH_MAX_VAL);
            RegisterFloat(_higherVelocityBarrier);
            
            _launchSpeedMultiplier = new JSONStorableFloat("launchSpeedMultiplier", 1.0f, 0.01f, 2.0f);
            RegisterFloat(_launchSpeedMultiplier);
            
            _pauseLaunchMessages = new JSONStorableBool("pauseLaunchMessages", true);
            RegisterBool(_pauseLaunchMessages);
            
            _simulatorPosition = new JSONStorableFloat("simulatorPosition", 0.0f, 0.0f, LAUNCH_MAX_VAL);
            RegisterFloat(_simulatorPosition);
            
            _debugVelocity = new JSONStorableFloat("launchVelocity", 0.0f, -LAUNCH_MAX_VAL, LAUNCH_MAX_VAL);
            RegisterFloat(_debugVelocity);

            _targetAtomChooser = new JSONStorableStringChooser("targetAtom", GetAtomChoices(), "", "Target Atom",
                (name) =>
                {
                    _targetAtom = SuperController.singleton.GetAtomByUid(name);
                    if (_targetAtom == null)
                    {
                        _targetController = null;
                    }
                    else
                    {
                        _targetController = _targetAtom.GetStorableByID(_targetControllerChooser.val) as FreeControllerV3;
                    }
                    
                    if (_targetController == null)
                    {
                        _targetControllerChooser.SetVal("");
                    }
                });
            RegisterStringChooser(_targetAtomChooser);
            
            _targetControllerChooser = new JSONStorableStringChooser("targetController", GetControllerChoices(), "", "Target Control",
                (name) =>
                {
                    if (_targetAtom == null)
                    {
                        _targetController = null;
                    }
                    else
                    {
                        _targetController = _targetAtom.GetStorableByID(name) as FreeControllerV3;
                    }
                });
            RegisterStringChooser(_targetControllerChooser);
        }

        private List<string> GetAtomChoices()
        {
            return SuperController.singleton.GetAtomUIDs();
        }

        private List<string> GetControllerChoices()
        {
            List<string> result = new List<string>();
            if (_targetAtom == null)
            {
                return result;
            }

            foreach (var sid in _targetAtom.GetStorableIDs())
            {
                var con = _targetAtom.GetStorableByID(sid) as FreeControllerV3;
                if (con != null)
                {
                    result.Add(sid);
                }
            }
            
            return result;
        }
        
        private void InitMenu()
        {
            CreateButton("Select VAM Launch Target").button.onClick.AddListener(() =>
            {
                SuperController.singleton.SelectModeAtom((atom) =>
                {
                    if (atom == null)
                    {
                        
                        return;
                    }

                    _targetAtomChooser.SetVal(atom.uid);
                });
            });
            
            UIDynamicPopup popup = CreateScrollablePopup(_targetAtomChooser);
            popup.popup.onOpenPopupHandlers += () =>
            {
                _targetAtomChooser.choices = GetAtomChoices();
            };
            
            popup = CreateScrollablePopup(_targetControllerChooser);
            popup.popup.onOpenPopupHandlers += () =>
            {
                _targetControllerChooser.choices = GetControllerChoices();
            };
            
            var slider = CreateSlider(_targetZoneWidth, true);
            slider.label = "Target Zone Width";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _targetZoneWidth.SetVal(v);
            });
            
            slider = CreateSlider(_targetZoneHeight, true);
            slider.label = "Target Zone Height";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _targetZoneHeight.SetVal(v);
            });
            
            slider = CreateSlider(_targetZoneDepth, true);
            slider.label = "Target Zone Depth";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _targetZoneDepth.SetVal(v);
            });
            
            slider = CreateSlider(_minLaunchSignalTimeThreshold, true);
            slider.label = "Min Adjust Time Threshold";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _minLaunchSignalTimeThreshold.SetVal(v);
            });
            
            slider = CreateSlider(_maxLaunchSignalTimeThreshold, true);
            slider.label = "Max Adjust Time Threshold";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _maxLaunchSignalTimeThreshold.SetVal(v);
            });
            
            slider = CreateSlider(_lowerVelocityBarrier, true);
            slider.label = "Min Adjust Time Vel Barrier";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _lowerVelocityBarrier.SetVal(v);
            });
            
            slider = CreateSlider(_higherVelocityBarrier, true);
            slider.label = "Max Adjust Time Vel Barrier";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _higherVelocityBarrier.SetVal(v);
            });
            
            slider = CreateSlider(_launchSpeedMultiplier, true);
            slider.label = "Launch Speed Multiplier";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _launchSpeedMultiplier.SetVal(v);
            });
            
            CreateSpacer();
            
            slider = CreateSlider(_currentLaunchSignalTimeThreshold, false);
            slider.label = "Adjust Time";
            
            slider = CreateSlider(_currentLaunchPos, false);
            slider.label = "Current Launch Position";
            
//            slider = CreateSlider(_debugVelocity, false);
//            slider.label = "Velocity";

            CreateSpacer();
            
            var toggle = CreateToggle(_pauseLaunchMessages);
            toggle.label = "Pause Launch";
            
            slider = CreateSlider(_simulatorPosition, false);
            slider.label = "Simulator";
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
            RenderEditorControls();
            UpdateZoneMatrices();
            UpdateLaunchPosition();
            UpdateNetwork();
            UpdateSimulator();
        }

        private void UpdateSimulator()
        {
            var pos = _simulatorPosition.val;

            var relativePos = pos / LAUNCH_MAX_VAL;
            var relativeTarget = _simulatorTarget / LAUNCH_MAX_VAL;
            var relativeSpeed = _simulatorSpeed / LAUNCH_MAX_VAL;

            relativePos = Mathf.MoveTowards(relativePos, relativeTarget,
                relativeSpeed * LAUNCH_LENGTHS_PER_SECOND * Time.deltaTime);
            _simulatorPosition.SetVal(relativePos * LAUNCH_MAX_VAL);
        }

        private void SetSimulatorTarget(float pos, float speed)
        {
            _simulatorTarget = Mathf.Clamp(pos, 0.0f, LAUNCH_MAX_VAL);
            _simulatorSpeed = Mathf.Clamp(speed, 0.0f, LAUNCH_MAX_VAL);
        }
        
        private void UpdateZoneMatrices()
        {
            if (_controller == null)
            {
                return;
            }

            _zoneRenderMatrix = Matrix4x4.TRS(_controller.transform.position, _controller.transform.rotation,
                new Vector3(_targetZoneWidth.val, _targetZoneHeight.val, _targetZoneDepth.val) * ZONE_MESH_SCALAR);
            _zoneInvMatrix = Matrix4x4.TRS(_controller.transform.position, _controller.transform.rotation,
                new Vector3(_targetZoneWidth.val, _targetZoneHeight.val, _targetZoneDepth.val)).inverse;
        }
        
        private void RenderEditorControls()
        {
            if (_controller != null && _controller.selected && SuperController.singleton.editModeToggle.isOn)
            {
                Graphics.DrawMesh(_controller.holdPositionMesh, _zoneRenderMatrix, _zoneMaterial,
                    _controller.gameObject.layer, null, 0, null, false, false);

                if (_targetAtom != null && _targetController != null)
                {
                    _lineDrawer.SetLinePoints(_controller.transform.position, _targetController.transform.position);
                    _lineDrawer.Draw();
                }
            }
        }

        private float CalculateAverageSpeed()
        {
            var averageVel = 0.0f;
            for (int i = 0; i < _velocityHistory.Count; i++)
            {
                averageVel += _velocityHistory[i];
            }

            if (_velocityHistory.Count > 0)
            {
                averageVel /= _velocityHistory.Count;
            }

            return averageVel;
        }

        // The signal threshold is how much time must moving in a single direction before the system will consider it
        // a valid motion to tell the launch about.
        private void UpdateSignalThreshold()
        {
            var averageVel = CalculateAverageSpeed();
            
            // To calculate this threshold we take the average speed and use it to blend between two min,max threshold
            // values, if the average speed is higher we want the threshold to be shorter, and if slower we want the threshold
            // to be longer to give the best chance of choosing a good velocity.
            _currentLaunchSignalTimeThreshold.SetVal(Mathf.Lerp(_maxLaunchSignalTimeThreshold.val,
                _minLaunchSignalTimeThreshold.val,
                Mathf.InverseLerp(_lowerVelocityBarrier.val, _higherVelocityBarrier.val, averageVel)));
        }

        private float RetrieveHighestSpeed(Queue<float> speeds)
        {
            float highestSpeed = 0.0f;
            while (speeds.Count > 0)
            {
                var spd = speeds.Dequeue();
                if (spd > highestSpeed)
                {
                    highestSpeed = spd;
                }
            }

            return highestSpeed;
        }
        
        private void UpdateLaunchPosition()
        {
            if (_targetAtom == null || _targetController == null)
            {
                return;
            }

            // Convert target position into zone local space
            Vector3 localZonePos = _zoneInvMatrix.MultiplyPoint(_targetController.transform.position);
            if (localZonePos.x < -1.0f || localZonePos.x > 1.0f || 
                localZonePos.y < -1.0f || localZonePos.y > 1.0f ||
                localZonePos.z < -1.0f || localZonePos.z > 1.0f)
            {
                return;
            }

            // Convert Y range from -1.0, 1.0 to 0.0f, 99.0f
            float posFactor = Mathf.InverseLerp(-1.0f, 1.0f, localZonePos.y);
            _currentLaunchPos.SetVal(Mathf.Lerp(0, LAUNCH_MAX_VAL, posFactor));

            // Calculate required launch move speed in order to reach new position
            float speed = PredictMoveSpeed(_lastLaunchPos, _currentLaunchPos.val, Time.deltaTime);

            // We store this speed into our velocity buffer to help with generating a rolling average speed.
            if (_velocityHistory.Count == AVG_VELOCITY_BUFFER_CAPACITY)
            {
                _velocityHistory.RemoveAt(0);
            }
            _velocityHistory.Add(speed);
            
            float delta = _currentLaunchPos.val - _lastLaunchPos;
            int deltaSign = delta > 0.0f ? 1 : -1;
            
            // We store the speed here so we can see it visually in the menu (for debugging)
            _debugVelocity.SetVal(speed * deltaSign);
            
            // We apply the users desired speed multiplier (After we store the original in for the rolling average to
            // prevent throwing off all the other parameters that tune the prediction logic)
            speed = Mathf.Clamp(speed * _launchSpeedMultiplier.val, 0.0f, LAUNCH_MAX_VAL);
            
            // Are we moving up?
            if (deltaSign == 1)
            {
                // Storing a rolling velocity for upwards motion
                if (_upwardsVelocityBuffer.Count == VELOCITY_BUFFER_CAPACITY)
                {
                    _upwardsVelocityBuffer.Dequeue();
                }
                _upwardsVelocityBuffer.Enqueue(speed);
               
                // If we continue moving in the same direction then we accumulate time, if that time goes over our
                // calculated threshold then we should consider telling the Launch about this movement
                var prevTime = _timeMovingUpwards;
                _timeMovingUpwards += Time.deltaTime;
               
                if (prevTime < _currentLaunchSignalTimeThreshold.val && 
                    _timeMovingUpwards >= _currentLaunchSignalTimeThreshold.val)
                {
                    // We take the highest speed during this upwards motion as it is most likely the highest speed that
                    // will be the closest representation of the motion.
                    float highestSpeed = RetrieveHighestSpeed(_upwardsVelocityBuffer);                    
                    highestSpeed = Mathf.Round(highestSpeed);

                    // Only if the speed is small do we tell the launch to go to exact locations 
                    // (Not happy with this, need to think of a better way to deal with slower motions)
                    float launchPos = highestSpeed > 1.0f ? LAUNCH_MAX_VAL : _currentLaunchPos.val;
                    float launchSpeed = highestSpeed;
                    
                    // Tell the Launch to do it's thing!
                    SendLaunchPosition((byte)launchPos, (byte)launchSpeed);

                    UpdateSignalThreshold();
                    
                    if (highestSpeed < 1.0f)
                    {
                        _timeMovingUpwards = 0.0f;
                        _upwardsVelocityBuffer.Clear();
                    }
                }
                
                _timeMovingDownwards = 0.0f;
                _downwardsVelocityBuffer.Clear();
            }
            // This does the same as above except for downwards motion
            else if (deltaSign == -1)
            {
                if (_downwardsVelocityBuffer.Count == VELOCITY_BUFFER_CAPACITY)
                {
                    _downwardsVelocityBuffer.Dequeue();
                }
                _downwardsVelocityBuffer.Enqueue(speed);
                
                var prevTime = _timeMovingDownwards;
                _timeMovingDownwards += Time.deltaTime;
                
                if (prevTime < _currentLaunchSignalTimeThreshold.val && 
                    _timeMovingDownwards >= _currentLaunchSignalTimeThreshold.val)
                {
                    float highestSpeed = RetrieveHighestSpeed(_downwardsVelocityBuffer);

                    highestSpeed = Mathf.Round(highestSpeed);

                    float launchPos = highestSpeed > 1.0f ? 0 : _currentLaunchPos.val;
                    float launchSpeed = highestSpeed;
                    
                    SendLaunchPosition((byte)launchPos, (byte)launchSpeed);
                    
                    UpdateSignalThreshold();

                    if (highestSpeed < 1.0f)
                    {
                        _timeMovingDownwards = 0.0f;
                        _downwardsVelocityBuffer.Clear();
                    }
                }
                
                _timeMovingUpwards = 0.0f;
                _upwardsVelocityBuffer.Clear();
            }
            
            _lastLaunchPos = _currentLaunchPos.val;
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

        // We use the fact that the launch can do 6 full lengths in one second to aid in calculating what speed
        // to send to the device.
        private float PredictMoveSpeed(float prevPos, float currPos, float duration)
        {
            float delta = currPos - prevPos;
            float dist = Mathf.Abs(delta);

            float relativeDist = dist / LAUNCH_MAX_VAL;
            float durationAtFullSpeed = relativeDist / LAUNCH_LENGTHS_PER_SECOND;
            
            float requiredSpeed = durationAtFullSpeed / duration;
            
            return Mathf.Clamp(requiredSpeed * LAUNCH_MAX_VAL, 0.0f, LAUNCH_MAX_VAL);
        }

        private static byte[] _launchData = new byte[2];
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
                _network.Send(_launchData, _launchData.Length);
            }
        }
    }
}