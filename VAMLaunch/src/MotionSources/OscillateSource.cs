using System.Collections.Generic;
using UnityEngine;

namespace VAMLaunchPlugin.MotionSources
{
    public class OscillateSource : IMotionSource
    {
        private abstract class RandomSource
        {
            public virtual void InitStorables(VAMLaunch plugin) { }
            public virtual void OnInit(VAMLaunch plugin) { }
            public virtual void OnDestroy(VAMLaunch plugin) { }
            
            public virtual void Update() {}
            public virtual float GetSpeedRange(float min, float max) { return 0.0f;}
            public virtual float GetPosRange(float min, float max) { return 0.0f;}
        }

        private class PureRandomSource : RandomSource
        {
            public override float GetSpeedRange(float min, float max)
            {
                return Random.Range(min, max);
            }
            public override float GetPosRange(float min, float max)
            {
                return Random.Range(min, max);
            }
        }

        private class PerlinRandomSource : RandomSource
        {
            private JSONStorableFloat _perlinOctave0;
            private JSONStorableFloat _perlinOctave1;
            private JSONStorableFloat _perlinValue0;
            private JSONStorableFloat _perlinValue1;
            private float _randomTimeOffset0;
            private float _randomTimeOffset1;

            public override void InitStorables(VAMLaunch plugin)
            {
                _perlinOctave0 = new JSONStorableFloat("oscPerlinOctave0", 0.25f, 0.0f, 2.0f);
                plugin.RegisterFloat(_perlinOctave0);
                _perlinOctave1 = new JSONStorableFloat("oscPerlinOctave1", 0.25f, 0.0f, 2.0f);
                plugin.RegisterFloat(_perlinOctave1);
                
                _perlinValue0 = new JSONStorableFloat("oscPerlinValue0", 0.0f, 0.0f, 1.0f);
                _perlinValue1 = new JSONStorableFloat("oscPerlinValue1", 0.0f, 0.0f, 1.0f);
            }

            public override void OnInit(VAMLaunch plugin)
            {
                var slider = plugin.CreateSlider(_perlinOctave0, true);
                slider.label = "Speed Perlin Octave";
                slider.slider.onValueChanged.AddListener((v) =>
                {
                    _perlinOctave0.SetVal(v);
                });
                
                slider = plugin.CreateSlider(_perlinValue0, true);
                slider.label = "Speed Perlin Value";
                
                slider = plugin.CreateSlider(_perlinOctave1, true);
                slider.label = "Position Perlin Octave";
                slider.slider.onValueChanged.AddListener((v) =>
                {
                    _perlinOctave1.SetVal(v);
                });
                
                slider = plugin.CreateSlider(_perlinValue1, true);
                slider.label = "Position Perlin Value";

                _randomTimeOffset0 = Time.time * Random.Range(1.0f, 2.0f);
                _randomTimeOffset1 = Time.time * Random.Range(1.0f, 2.0f);
            }

            public override void OnDestroy(VAMLaunch plugin)
            {
                plugin.RemoveSlider(_perlinOctave0);
                plugin.RemoveSlider(_perlinValue0);
                plugin.RemoveSlider(_perlinOctave1);
                plugin.RemoveSlider(_perlinValue1);
            }

            public override void Update()
            {
                _perlinValue0.SetVal(GetSpeedRange(0.0f, 1.0f));
                _perlinValue1.SetVal(GetPosRange(0.0f, 1.0f));
            }

            public override float GetSpeedRange(float min, float max)
            {
                return Mathf.Lerp(min, max,
                    Mathf.PerlinNoise(Time.time * _perlinOctave0.val + _randomTimeOffset0, 0.0f));
            }
            
            public override float GetPosRange(float min, float max)
            {
                return Mathf.Lerp(min, max,
                    Mathf.PerlinNoise(Time.time * _perlinOctave1.val + _randomTimeOffset1, 0.0f));
            }
        }
        
        private const float LAUNCH_DIR_CHANGE_DELAY = 0.02f;

        private VAMLaunch _plugin;
        
        private JSONStorableFloat _minPosition;
        private JSONStorableFloat _maxPosition;
        private JSONStorableBool _posRangeAffectsAnimation;
        private JSONStorableFloat _speed;
        private JSONStorableFloat _animationOffset;
        private JSONStorableStringChooser _targetAnimationAtomChooser;
        private JSONStorableFloat _randomSpeedOffset;
        private JSONStorableFloat _randomPosOffset;
        private JSONStorableBool _randomEnabled;
        private JSONStorableStringChooser _randomSourceChooser;

        private RandomSource _currentRandomSource;
        private int _currentRandomSourceIndex = -1;
        private int _desiredRandomSourceIndex;
        
        private List<string> _randomSourceChoices = new List<string>
        {
            "Pure Random",
            "Perlin"
        };

        private List<RandomSource> _randomSources = new List<RandomSource>
        {
            new PureRandomSource(),
            new PerlinRandomSource(),
        };
        
        private UIDynamicPopup _chooseAnimationAtomPopup;

        private bool _moveUpwards = true;
        private float _dirChangeTimer;
        private float _dirChangeDuration;
        private float _previousPosition;

        private FreeControllerV3 _pluginFreeController;
        private FreeControllerV3 _animationAtomController;
        
        private AnimationPattern _targetAnimationPattern;
        private JSONStorableFloat _animationCurrentTime;
        private JSONStorableFloat _animationSpeed;
        
        private LineDrawer _lineDrawer0;
        
        public void OnInit(VAMLaunch plugin)
        {
            _plugin = plugin;
            
            _pluginFreeController = plugin.containingAtom.GetStorableByID("control") as FreeControllerV3;
            
            InitOptionsUI(plugin);
            InitEditorGizmos();

            _moveUpwards = true;
            _dirChangeTimer = 0.0f;

            foreach (var rs in _randomSources)
            {
                rs.InitStorables(plugin);
            }
        }

        public void OnInitStorables(VAMLaunch plugin)
        {
            _minPosition = new JSONStorableFloat("oscSourceMinPosition", 10.0f, 0.0f, 99.0f);
            plugin.RegisterFloat(_minPosition);
            _maxPosition = new JSONStorableFloat("oscSourceMaxPosition", 80.0f, 0.0f, 99.0f);
            plugin.RegisterFloat(_maxPosition);
            _posRangeAffectsAnimation = new JSONStorableBool("oscPosRangeAffectsAnimation", false);
            plugin.RegisterBool(_posRangeAffectsAnimation);
            _speed = new JSONStorableFloat("oscSourceSpeed", 30.0f, 20.0f, 80.0f);
            plugin.RegisterFloat(_speed);
            _animationOffset = new JSONStorableFloat("oscAnimationOffset", 0.0f, 0.0f, 0.5f);
            plugin.RegisterFloat(_animationOffset);
            _randomSpeedOffset = new JSONStorableFloat("oscRandomSpeedOffset", 0.0f, 0.0f, 99.0f);
            plugin.RegisterFloat(_randomSpeedOffset);
            _randomPosOffset = new JSONStorableFloat("oscRandomPosOffset", 0.0f, 0.0f, 50.0f);
            plugin.RegisterFloat(_randomPosOffset);
            _randomEnabled = new JSONStorableBool("oscRandomEnabled", false);
            plugin.RegisterBool(_randomEnabled);
            
            _randomSourceChooser = new JSONStorableStringChooser("oscRandomSource", _randomSourceChoices, "",
                "Random Source",
                (string name) => { _desiredRandomSourceIndex = _randomSourceChoices.IndexOf(name); });
            _randomSourceChooser.choices = _randomSourceChoices;
            plugin.RegisterStringChooser(_randomSourceChooser);
            if (string.IsNullOrEmpty(_randomSourceChooser.val))
            {
                _randomSourceChooser.SetVal(_randomSourceChoices[0]);
            }

            _targetAnimationAtomChooser = new JSONStorableStringChooser("oscSourceTargetAnimationAtom",
                GetTargetAnimationAtomChoices(), "", "Target Animation Pattern",
                (name) =>
                {
                    _animationAtomController = null;
                    _targetAnimationPattern = null;

                    if (string.IsNullOrEmpty(name))
                    {
                        return;
                    }

                    var atom = SuperController.singleton.GetAtomByUid(name);
                    if (atom && atom.animationPatterns.Length > 0)
                    {
                        _animationAtomController = atom.freeControllers[0];
                        _targetAnimationPattern = atom.animationPatterns[0];
                        _animationCurrentTime = _targetAnimationPattern.GetFloatJSONParam("currentTime");
                        _animationSpeed = _targetAnimationPattern.GetFloatJSONParam("speed");
                    }
                });
            plugin.RegisterStringChooser(_targetAnimationAtomChooser);
        }
        
        private List<string> GetTargetAnimationAtomChoices()
        {
            List<string> result = new List<string>();
            result.Add("None");
            foreach (var uid in SuperController.singleton.GetAtomUIDs())
            {
                var atom = SuperController.singleton.GetAtomByUid(uid);
                if (atom.animationPatterns.Length > 0)
                {
                    result.Add(uid);
                }
            }

            return result;
        }

        private void InitOptionsUI(VAMLaunch plugin)
        {
            var slider = plugin.CreateSlider(_minPosition, true);
            slider.label = "Min Position";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _minPosition.SetVal(Mathf.Min(_maxPosition.val - 20.0f, v));
            });
            
            slider = plugin.CreateSlider(_maxPosition, true);
            slider.label = "Max Position";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _maxPosition.SetVal(Mathf.Max(_minPosition.val + 20.0f, v));
            });
            slider = plugin.CreateSlider(_speed, true);
            slider.label = "Speed";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _speed.SetVal(v);
            });

            var toggle = plugin.CreateToggle(_randomEnabled, true);
            toggle.label = "Random Enabled";
            
            slider = plugin.CreateSlider(_randomSpeedOffset, true);
            slider.label = "Random Speed Offset";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _randomSpeedOffset.SetVal(v);
            });
            
            slider = plugin.CreateSlider(_randomPosOffset, true);
            slider.label = "Random Position Offset";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _randomPosOffset.SetVal(v);
            });
            
            plugin.CreateScrollablePopup(_randomSourceChooser, true);
            
            _chooseAnimationAtomPopup = plugin.CreateScrollablePopup(_targetAnimationAtomChooser);
            _chooseAnimationAtomPopup.popup.onOpenPopupHandlers += () =>
            {
                _targetAnimationAtomChooser.choices = GetTargetAnimationAtomChoices();
            };
            
            slider = plugin.CreateSlider(_animationOffset, false);
            slider.label = "Animation Offset";
            slider.slider.onValueChanged.AddListener((v) =>
            {
                _animationOffset.SetVal(v);
            });
            
            toggle = plugin.CreateToggle(_posRangeAffectsAnimation, false);
            toggle.label = "Pos Range Affects Animation";
        }

        private void DestroyOptionsUI(VAMLaunch plugin)
        {
            plugin.RemoveSlider(_minPosition);
            plugin.RemoveSlider(_maxPosition);
            plugin.RemoveSlider(_speed);
            plugin.RemoveToggle(_randomEnabled);
            plugin.RemoveSlider(_randomSpeedOffset);
            plugin.RemoveSlider(_randomPosOffset);
            plugin.RemoveSlider(_animationOffset);
            plugin.RemovePopup(_chooseAnimationAtomPopup);
        }
        
        private void InitEditorGizmos()
        {
            _lineDrawer0 = new LineDrawer(_pluginFreeController.linkLineMaterial);
        }

        private void UpdateRandomSource()
        {
            if (_desiredRandomSourceIndex != _currentRandomSourceIndex)
            {
                if (_currentRandomSource != null)
                {
                    _currentRandomSource.OnDestroy(_plugin);
                    _currentRandomSource = null;
                }

                if (_desiredRandomSourceIndex >= 0)
                {
                    _currentRandomSource = _randomSources[_desiredRandomSourceIndex];
                    _currentRandomSource.OnInit(_plugin);
                }

                _currentRandomSourceIndex = _desiredRandomSourceIndex;
            }

            if (_currentRandomSource != null)
            {
                _currentRandomSource.Update();
            }
        }
        
        public bool OnUpdate(ref byte outPos, ref byte outSpeed)
        {
            UpdateRandomSource();
            
            _dirChangeTimer -= Time.deltaTime;
            if (_dirChangeTimer <= 0.0f)
            {
                _moveUpwards = !_moveUpwards;

                float target = _moveUpwards ? 99.0f : 0.0f;
                if (_randomEnabled.val && _currentRandomSource != null)
                {
                    float posOffset = _currentRandomSource.GetPosRange(0.0f, _randomPosOffset.val);
                    target += _moveUpwards ? -posOffset : posOffset;
                }
                target = Mathf.Clamp(target, _minPosition.val, _maxPosition.val);

                float finalSpeed = _speed.val;
                if (_randomEnabled.val && _currentRandomSource != null)
                {
                    finalSpeed += _randomSpeedOffset.val * _currentRandomSource.GetSpeedRange(-1.0f, 1.0f);
                    finalSpeed = Mathf.Clamp(finalSpeed, _speed.min, _speed.max);
                }
                
                float dist = Mathf.Abs(target - _previousPosition);
                _dirChangeDuration = LaunchUtils.PredictMoveDuration(dist, finalSpeed) + LAUNCH_DIR_CHANGE_DELAY;
                _dirChangeTimer = _dirChangeDuration - Mathf.Min(_dirChangeDuration, -_dirChangeTimer);

                _previousPosition = target;
                
                outPos = (byte) target;
                outSpeed = (byte) finalSpeed;

                return true;
            }

            return false;
        }
        
        public void OnSimulatorUpdate(float prevPos, float newPos, float deltaTime)
        {
            if (_targetAnimationPattern == null)
            {
                if (!string.IsNullOrEmpty(_targetAnimationAtomChooser.val))
                {
                    _targetAnimationAtomChooser.SetVal("");
                }
                
                return;
            }

            if (_pluginFreeController.selected && SuperController.singleton.editModeToggle.isOn)
            {
                _lineDrawer0.SetLinePoints(_pluginFreeController.transform.position,
                    _animationAtomController.transform.position);
                _lineDrawer0.Draw();
            }
            
            float totalTime = _targetAnimationPattern.GetTotalTime();
            
            float normOldTime = _animationCurrentTime.val / totalTime;

            float normalizedLaunchPos = _posRangeAffectsAnimation.val
                ? Mathf.InverseLerp(0.0f, 99.0f, newPos)
                : Mathf.InverseLerp(_minPosition.val, _maxPosition.val, newPos);

            float newTime;
            if (_moveUpwards)
            {
                newTime = Mathf.Lerp(0.0f, totalTime * 0.5f, normalizedLaunchPos);
            }
            else
            {
                newTime = Mathf.Lerp(totalTime, totalTime * 0.5f, normalizedLaunchPos);
            }

            newTime = (newTime + (totalTime * _animationOffset.val)) % totalTime;

            float normNewTime = newTime / totalTime;
            
            if (Mathf.Abs(normNewTime - normOldTime) > 0.05f)
            {
                _animationCurrentTime.SetVal(newTime);
            }

            _animationSpeed.SetVal(totalTime / (_dirChangeDuration * 2.0f) * (_moveUpwards ? 1.0f : -1.0f));
        }

        public void OnDestroy(VAMLaunch plugin)
        {
            DestroyOptionsUI(plugin);
        }
    }
}