using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VAMLaunchPlugin.MotionSources
{
    public class ZoneSource : IMotionSource
    {
        public const int VELOCITY_BUFFER_CAPACITY = 20;
        public const int AVG_VELOCITY_BUFFER_CAPACITY = 100;
        
        private const float ZONE_MESH_SCALAR = 20.0f;
        
        private JSONStorableFloat _currentTargetPos;
        private JSONStorableFloat _positionSampleRate;
        private JSONStorableFloat _minLaunchSignalTimeThreshold;
        private JSONStorableFloat _maxLaunchSignalTimeThreshold;
        private JSONStorableFloat _currentLaunchSignalTimeThreshold;
        private JSONStorableFloat _lowerVelocityBarrier;
        private JSONStorableFloat _higherVelocityBarrier;
        private JSONStorableFloat _launchSpeedMultiplier;
        private JSONStorableFloat _targetZoneWidth;
        private JSONStorableFloat _targetZoneHeight;
        private JSONStorableFloat _targetZoneDepth;
        private JSONStorableStringChooser _targetAtomChooser;
        private JSONStorableStringChooser _targetControllerChooser;
        
        private UIDynamicButton _chooseAtomButton;
        private UIDynamicPopup _chooseAtomPopup;
        private UIDynamicPopup _chooseControlPopup;
        
        private FreeControllerV3 _pluginFreeController;
        private FreeControllerV3 _zoneFreeController;
        
        private Atom _targetAtom;
        private FreeControllerV3 _targetController;
        
        private float _lastLaunchPos;
        private float _positionSampleTimer;
        private float _timeMovingUpwards;
        private float _timeMovingDownwards;
        
        private Matrix4x4 _zoneRenderMatrix;
        private Matrix4x4 _zoneInvMatrix;

        private Queue<float> _upwardsVelocityBuffer = new Queue<float>(VELOCITY_BUFFER_CAPACITY);
        private Queue<float> _downwardsVelocityBuffer = new Queue<float>(VELOCITY_BUFFER_CAPACITY);
        private List<float> _velocityHistory = new List<float>(AVG_VELOCITY_BUFFER_CAPACITY);

        private LineDrawer _lineDrawer0, _lineDrawer1;
        private Material _zoneMaterial;
        private Material _targetPosMaterial;
        
        public void OnInit(VAMLaunch plugin)
        {
            _pluginFreeController = plugin.containingAtom.GetStorableByID("control") as FreeControllerV3;
            
            InitOptionsUI(plugin);
            InitEditorGizmos();

            plugin.StartCoroutine(InitZoneAtom());
        }

        public bool OnUpdate(ref byte outPos, ref byte outSpeed)
        {
            UpdateZoneMatrices();

            bool shouldMoveLaunch = ProcessTarget(ref outPos, ref outSpeed);
            
            RenderEditorGizmos();

            return shouldMoveLaunch;
        }
        
        public void OnSimulatorUpdate(float prevPos, float newPos, float deltaTime)
        {
            
        }

        private bool ProcessTarget(ref byte outPos, ref byte outSpeed)
        {
            bool shouldMoveLaunch = false;
            
            if (_targetAtom == null || _targetController == null)
            {
                return false;
            }

            _positionSampleTimer -= Time.deltaTime;
            if (_positionSampleTimer > 0.0f)
            {
                return false;
            }

            var sampleInterval = GetPositionSampleInterval();
            _positionSampleTimer = sampleInterval - Mathf.Min(sampleInterval, -_positionSampleTimer);

            // Convert target position into zone local space
            Vector3 localZonePos = _zoneInvMatrix.MultiplyPoint(_targetController.transform.position);
            if (localZonePos.x < -1.0f || localZonePos.x > 1.0f || 
                localZonePos.y < -1.0f || localZonePos.y > 1.0f ||
                localZonePos.z < -1.0f || localZonePos.z > 1.0f)
            {
                return false;
            }

            // Convert Y range from -1.0, 1.0 to 0.0f, 99.0f
            float posFactor = Mathf.InverseLerp(-1.0f, 1.0f, localZonePos.y);
            _currentTargetPos.SetVal(Mathf.Lerp(0, LaunchUtils.LAUNCH_MAX_VAL, posFactor));

            // Calculate required launch move speed in order to reach new position
            float speed = LaunchUtils.PredictMoveSpeed(_lastLaunchPos, _currentTargetPos.val, Time.deltaTime);

            // We store this speed into our velocity buffer to help with generating a rolling average speed.
            if (_velocityHistory.Count == AVG_VELOCITY_BUFFER_CAPACITY)
            {
                _velocityHistory.RemoveAt(0);
            }
            _velocityHistory.Add(speed);
            
            float delta = _currentTargetPos.val - _lastLaunchPos;
            int deltaSign = delta > 0.0f ? 1 : -1;
            
            // We apply the users desired speed multiplier (After we store the original in for the rolling average to
            // prevent throwing off all the other parameters that tune the prediction logic)
            speed = Mathf.Clamp(speed * _launchSpeedMultiplier.val, 0.0f, LaunchUtils.LAUNCH_MAX_VAL);
            
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
                    float launchPos = highestSpeed > 1.0f ? LaunchUtils.LAUNCH_MAX_VAL : _currentTargetPos.val;
                    float launchSpeed = highestSpeed;

                    // Tell the Launch to do it's thing!
                    shouldMoveLaunch = true;
                    outPos = (byte)launchPos;
                    outSpeed = (byte) launchSpeed;

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

                    float launchPos = highestSpeed > 1.0f ? 0 : _currentTargetPos.val;
                    float launchSpeed = highestSpeed;
                    
                    // Tell the Launch to do it's thing!
                    shouldMoveLaunch = true;
                    outPos = (byte)launchPos;
                    outSpeed = (byte) launchSpeed;
                    
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
            
            _lastLaunchPos = _currentTargetPos.val;
            
            return shouldMoveLaunch;
        }

        public void OnDestroy(VAMLaunch plugin)
        {
            DestroyOptionsUI(plugin);
        }

        public void OnInitStorables(VAMLaunch plugin)
        {
            _targetZoneWidth = new JSONStorableFloat("zoneSourceTargetZoneWidth", 0.1f, 0.005f, 0.2f);
            _targetZoneHeight = new JSONStorableFloat("zoneSourceTargetZoneHeight", 0.1f, 0.005f, 0.2f);
            _targetZoneDepth = new JSONStorableFloat("zoneSourceTargetZoneDepth", 0.1f, 0.005f, 0.2f);
            
            plugin.RegisterFloat(_targetZoneWidth);
            plugin.RegisterFloat(_targetZoneHeight);
            plugin.RegisterFloat(_targetZoneDepth);
            
            _currentTargetPos = new JSONStorableFloat("zoneSourceLaunchPos", 0.0f, 0.0f, LaunchUtils.LAUNCH_MAX_VAL);
            plugin.RegisterFloat(_currentTargetPos);
            
            _positionSampleRate = new JSONStorableFloat("zoneSourcePositionSampleRate", 40.0f, 10.0f, 90.0f);
            plugin.RegisterFloat(_positionSampleRate);

            _minLaunchSignalTimeThreshold =
                new JSONStorableFloat("zoneSourceMinLaunchSignalTimeThreshold", 0.1f, 0.001f, 0.4f);
            plugin.RegisterFloat(_minLaunchSignalTimeThreshold);

            _maxLaunchSignalTimeThreshold =
                new JSONStorableFloat("zoneSourceMaxLaunchSignalTimeThreshold", 0.25f, 0.001f, 0.4f);
            plugin.RegisterFloat(_maxLaunchSignalTimeThreshold);

            _currentLaunchSignalTimeThreshold =
                new JSONStorableFloat("zoneSourceCurrentLaunchSignalTimeThreshold", 0.099f, 0.001f, 0.4f);
            plugin.RegisterFloat(_currentLaunchSignalTimeThreshold);

            _lowerVelocityBarrier =
                new JSONStorableFloat("zoneSourceLowerVelocityBarrier", 10.0f, 0.0f, LaunchUtils.LAUNCH_MAX_VAL);
            plugin.RegisterFloat(_lowerVelocityBarrier);

            _higherVelocityBarrier = new JSONStorableFloat("zoneSourceHigherVelocityBarrier", 45.0f, 0.0f,
                LaunchUtils.LAUNCH_MAX_VAL);
            plugin.RegisterFloat(_higherVelocityBarrier);
            
            _launchSpeedMultiplier = new JSONStorableFloat("zoneSourceLaunchSpeedMultiplier", 1.0f, 0.01f, 2.0f);
            plugin.RegisterFloat(_launchSpeedMultiplier);

            _targetAtomChooser = new JSONStorableStringChooser("zoneSourceTargetAtom", GetTargetAtomChoices(), "",
                "Target Atom",
                (name) =>
                {
                    _targetAtom = SuperController.singleton.GetAtomByUid(name);
                    if (_targetAtom == null)
                    {
                        _targetController = null;
                    }
                    else
                    {
                        _targetController =
                            _targetAtom.GetStorableByID(_targetControllerChooser.val) as FreeControllerV3;
                    }

                    if (_targetController == null)
                    {
                        _targetControllerChooser.SetVal("");
                    }
                });
            plugin.RegisterStringChooser(_targetAtomChooser);

            _targetControllerChooser = new JSONStorableStringChooser("zoneSourceTargetController",
                GetTargetControllerChoices(), "", "Target Control",
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
            plugin.RegisterStringChooser(_targetControllerChooser);
        }

        private void InitOptionsUI(VAMLaunch plugin)
        {
            _chooseAtomButton = plugin.CreateButton("Select Zone Target");
            _chooseAtomButton.button.onClick.AddListener(() =>
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
            
            _chooseAtomPopup = plugin.CreateScrollablePopup(_targetAtomChooser);
            _chooseAtomPopup.popup.onOpenPopupHandlers += () =>
            {
                _targetAtomChooser.choices = GetTargetAtomChoices();
            };
            
            _chooseControlPopup = plugin.CreateScrollablePopup(_targetControllerChooser);
            _chooseControlPopup.popup.onOpenPopupHandlers += () =>
            {
                _targetControllerChooser.choices = GetTargetControllerChoices();
            };
            
            var slider = plugin.CreateSlider(_targetZoneWidth, true);
            slider.label = "Target Zone Width";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _targetZoneWidth.SetVal(v);
            });
            
            slider = plugin.CreateSlider(_targetZoneHeight, true);
            slider.label = "Target Zone Height";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _targetZoneHeight.SetVal(v);
            });
            
            slider = plugin.CreateSlider(_targetZoneDepth, true);
            slider.label = "Target Zone Depth";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _targetZoneDepth.SetVal(v);
            });
            
            slider = plugin.CreateSlider(_positionSampleRate, true);
            slider.label = "Position Sample Rate";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _positionSampleRate.SetVal(v);
            });
            
            slider = plugin.CreateSlider(_minLaunchSignalTimeThreshold, true);
            slider.label = "Min Adjust Time Threshold";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _minLaunchSignalTimeThreshold.SetVal(v);
            });
            
            slider = plugin.CreateSlider(_maxLaunchSignalTimeThreshold, true);
            slider.label = "Max Adjust Time Threshold";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _maxLaunchSignalTimeThreshold.SetVal(v);
            });
            
            slider = plugin.CreateSlider(_lowerVelocityBarrier, true);
            slider.label = "Min Adjust Time Vel Barrier";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _lowerVelocityBarrier.SetVal(v);
            });
            
            slider = plugin.CreateSlider(_higherVelocityBarrier, true);
            slider.label = "Max Adjust Time Vel Barrier";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _higherVelocityBarrier.SetVal(v);
            });
            
            slider = plugin.CreateSlider(_launchSpeedMultiplier, true);
            slider.label = "Launch Speed Multiplier";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _launchSpeedMultiplier.SetVal(v);
            });
            
            plugin.CreateSpacer();
            
            slider = plugin.CreateSlider(_currentLaunchSignalTimeThreshold, false);
            slider.label = "Adjust Time";
            
            slider = plugin.CreateSlider(_currentTargetPos, false);
            slider.label = "Current Target Position";
        }

        private void DestroyOptionsUI(VAMLaunch plugin)
        {
            plugin.RemoveButton(_chooseAtomButton);
            plugin.RemovePopup(_chooseAtomPopup);
            plugin.RemovePopup(_chooseControlPopup);
            plugin.RemoveSlider(_targetZoneWidth);
            plugin.RemoveSlider(_targetZoneHeight);
            plugin.RemoveSlider(_targetZoneDepth);
            plugin.RemoveSlider(_positionSampleRate);
            plugin.RemoveSlider(_minLaunchSignalTimeThreshold);
            plugin.RemoveSlider(_maxLaunchSignalTimeThreshold);
            plugin.RemoveSlider(_lowerVelocityBarrier);
            plugin.RemoveSlider(_higherVelocityBarrier);
            plugin.RemoveSlider(_launchSpeedMultiplier);
            plugin.RemoveSlider(_currentLaunchSignalTimeThreshold);
            plugin.RemoveSlider(_currentTargetPos);
        }
        
        private IEnumerator InitZoneAtom()
        {
            const string ZoneAtomName = "LaunchZone";
            var zoneAtom = SuperController.singleton.GetAtomByUid(ZoneAtomName);
            if (zoneAtom == null)
            {
                yield return SuperController.singleton.AddAtomByType("Empty", ZoneAtomName);
                zoneAtom = SuperController.singleton.GetAtomByUid(ZoneAtomName);
            }
            
            if (zoneAtom != null)
            {
                var trigger = zoneAtom.GetComponent<CollisionTrigger>();
                if (trigger != null)
                {
                    Object.Destroy(trigger);
                }
                _zoneFreeController = zoneAtom.mainController;
            }
        }

        private void InitEditorGizmos()
        {
            _lineDrawer0 = new LineDrawer(_pluginFreeController.linkLineMaterial);
            _lineDrawer1 = new LineDrawer(_pluginFreeController.linkLineMaterial);
            
            _zoneMaterial = new Material(_pluginFreeController.linkLineMaterial);
            Color zoneColor = Color.magenta;
            zoneColor.a = 0.1f;
            _zoneMaterial.SetColor("_Color", zoneColor);
            
            _targetPosMaterial = new Material(_pluginFreeController.linkLineMaterial);
            Color posColor = Color.green;
            posColor.a = 0.1f;
            _targetPosMaterial.SetColor("_Color", posColor);
        }
        
        private List<string> GetTargetAtomChoices()
        {
            return SuperController.singleton.GetAtomUIDs();
        }
        
        private List<string> GetTargetControllerChoices()
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
        
        private void UpdateZoneMatrices()
        {
            if (_zoneFreeController == null)
            {
                return;
            }

            Vector3 zonePosition = _zoneFreeController.transform.position;
            Quaternion zoneRotation = _zoneFreeController.transform.rotation;
            
            _zoneRenderMatrix = Matrix4x4.TRS(zonePosition, zoneRotation,
                new Vector3(_targetZoneWidth.val, _targetZoneHeight.val, _targetZoneDepth.val) * ZONE_MESH_SCALAR);
            _zoneInvMatrix = Matrix4x4.TRS(zonePosition, zoneRotation,
                new Vector3(_targetZoneWidth.val, _targetZoneHeight.val, _targetZoneDepth.val)).inverse;
        }

        private void RenderEditorGizmos()
        {
            if (_zoneFreeController != null)
            {
                bool zoneSelected = _zoneFreeController.selected && SuperController.singleton.editModeToggle.isOn;
                bool controllerSelected = _pluginFreeController != null && _pluginFreeController.selected &&
                                          SuperController.singleton.editModeToggle.isOn;

                bool showZone = zoneSelected || controllerSelected;
                
                if (showZone)
                {
                    Graphics.DrawMesh(_zoneFreeController.holdPositionMesh, _zoneRenderMatrix, _zoneMaterial,
                        _zoneFreeController.gameObject.layer, null, 0, null, false, false);

                    float relTargetPos = _currentTargetPos.val / (99.0f * 2.0f);

                    Vector3 targetPosBoxScale = new Vector3(0.2f,
                        _targetZoneHeight.val * ZONE_MESH_SCALAR * relTargetPos * 2.0f, 0.2f);


                    Matrix4x4 targetPosMatrix = Matrix4x4.TRS(
                        _zoneFreeController.transform.position +
                        _zoneFreeController.transform.rotation * Vector3.right * (_targetZoneWidth.val + 0.01f) +
                        _zoneFreeController.transform.rotation * Vector3.down * _targetZoneHeight.val +
                        _zoneFreeController.transform.rotation * Vector3.up * _targetZoneHeight.val * relTargetPos *
                        2.0f,
                        _zoneFreeController.transform.rotation,
                        targetPosBoxScale);
                    
                    Graphics.DrawMesh(_zoneFreeController.holdPositionMesh, targetPosMatrix, _targetPosMaterial,
                        _zoneFreeController.gameObject.layer, null, 0, null, false, false);

                    if (_targetAtom != null && _targetController != null)
                    {
                        _lineDrawer0.SetLinePoints(_zoneFreeController.transform.position,
                            _targetController.transform.position);
                        _lineDrawer0.Draw();
                    }
                }

                if (controllerSelected)
                {
                    _lineDrawer1.SetLinePoints(_pluginFreeController.transform.position,
                        _zoneFreeController.transform.position);
                    _lineDrawer1.Draw();
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

        private float GetPositionSampleInterval()
        {
            return 1.0f / _positionSampleRate.val;
        }
    }
}