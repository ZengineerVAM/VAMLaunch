using System.Collections.Generic;
using UnityEngine;

namespace VAMLaunchPlugin.MotionSources
{
    public class PatternSource : IMotionSource
    {
        private struct MotionPoint
        {
            public float Time;
            public Vector3 Position;
        }
        
        private const int MOTION_POINTS_INITIAL_CAPACITY = 20;
        
        private JSONStorableFloat _minPosition;
        private JSONStorableFloat _maxPosition;
        private JSONStorableStringChooser _targetAnimationAtomChooser;
        private JSONStorableStringChooser _samplePlaneChooser;
        private JSONStorableBool _includeMidPoints;
        private JSONStorableBool _invertPosition;
        private JSONStorableBool _useLocalSpace;

        private JSONStorableFloat _patternTime;
        private JSONStorableFloat _patternSpeed;

        private UIDynamicPopup _chooseAnimationAtomPopup;
        private UIDynamicPopup _chooseSamplePlanePopup;

        private FreeControllerV3 _pluginFreeController;
        private FreeControllerV3 _animationAtomController;
        
        private AnimationPattern _targetAnimationPattern;
        
        private List<MotionPoint> _motionPoints = new List<MotionPoint>(MOTION_POINTS_INITIAL_CAPACITY);
        private float _lastLaunchPos;
        private int _lastPointIndex;
        
        private LineDrawer _lineDrawer0;

        private List<string> _samplePlaneChoices = new List<string>
        {
            "X",
            "Y",
            "Z"
        };

        private int _samplePlaneIndex;
        
        public void OnInit(VAMLaunch plugin)
        {
            _pluginFreeController = plugin.containingAtom.GetStorableByID("control") as FreeControllerV3;
            
            InitOptionsUI(plugin);
            InitEditorGizmos();
        }

        public void OnInitStorables(VAMLaunch plugin)
        {
            _minPosition = new JSONStorableFloat("patternSourceMinPosition", 10.0f, 0.0f, 99.0f);
            plugin.RegisterFloat(_minPosition);
            _maxPosition = new JSONStorableFloat("patternSourceMaxPosition", 80.0f, 0.0f, 99.0f);
            plugin.RegisterFloat(_maxPosition);

            _targetAnimationAtomChooser = new JSONStorableStringChooser("patternSourceTargetAnimationAtom",
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
                        _patternTime = _targetAnimationPattern.GetFloatJSONParam("currentTime");
                        _patternSpeed = _targetAnimationPattern.GetFloatJSONParam("speed");
                    }
                });
            plugin.RegisterStringChooser(_targetAnimationAtomChooser);

            _samplePlaneChooser = new JSONStorableStringChooser("patternSourceSamplePlane", _samplePlaneChoices, "",
                "Sample Plane", (name) =>
                {
                    for (int i = 0; i < _samplePlaneChoices.Count; i++)
                    {
                        if (_samplePlaneChoices[i] == name)
                        {
                            _samplePlaneIndex = i;
                            break;
                        }
                    }
                });
            plugin.RegisterStringChooser(_samplePlaneChooser);
            if (string.IsNullOrEmpty(_samplePlaneChooser.val))
            {
                _samplePlaneChooser.SetVal("Y");
            }
            
            _includeMidPoints = new JSONStorableBool("patternSourceIncludeMidPoints", false);
            plugin.RegisterBool(_includeMidPoints);
            _invertPosition = new JSONStorableBool("patternSourceInvertPosition", false);
            plugin.RegisterBool(_invertPosition);
            _useLocalSpace = new JSONStorableBool("patternSourceUseLocalSpace", false);
            plugin.RegisterBool(_useLocalSpace);
        }
        
        private List<string> GetTargetAnimationAtomChoices()
        {
            List<string> result = new List<string>();
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
            
            _chooseAnimationAtomPopup = plugin.CreateScrollablePopup(_targetAnimationAtomChooser);
            _chooseAnimationAtomPopup.popup.onOpenPopupHandlers += () =>
            {
                _targetAnimationAtomChooser.choices = GetTargetAnimationAtomChoices();
            };
            
            _chooseSamplePlanePopup = plugin.CreateScrollablePopup(_samplePlaneChooser);
            _chooseSamplePlanePopup.popup.onOpenPopupHandlers += () =>
            {
                _samplePlaneChooser.choices = _samplePlaneChoices;
            };
            
            var toggle = plugin.CreateToggle(_useLocalSpace);
            toggle.label = "Use Local Space";
            
            toggle = plugin.CreateToggle(_includeMidPoints);
            toggle.label = "Include Mid Points";
            
            toggle = plugin.CreateToggle(_invertPosition);
            toggle.label = "Invert";
            
            
        }

        private void DestroyOptionsUI(VAMLaunch plugin)
        {
            plugin.RemoveSlider(_minPosition);
            plugin.RemoveSlider(_maxPosition);
            plugin.RemovePopup(_chooseAnimationAtomPopup);
            plugin.RemovePopup(_samplePlaneChooser);
            plugin.RemoveToggle(_includeMidPoints);
            plugin.RemoveToggle(_invertPosition);
            plugin.RemoveToggle(_useLocalSpace);
        }
        
        private void InitEditorGizmos()
        {
            _lineDrawer0 = new LineDrawer(_pluginFreeController.linkLineMaterial);
        }

        public bool OnUpdate(ref byte outPos, ref byte outSpeed)
        {
            if (_targetAnimationPattern == null)
            {
                if (!string.IsNullOrEmpty(_targetAnimationAtomChooser.val))
                {
                    _targetAnimationAtomChooser.SetVal("");
                }
                return false;
            }

            if (_pluginFreeController.selected && SuperController.singleton.editModeToggle.isOn)
            {
                _lineDrawer0.SetLinePoints(_pluginFreeController.transform.position,
                    _animationAtomController.transform.position);
                _lineDrawer0.Draw();
            }
            
            if (_targetAnimationPattern.steps.Length <= 1)
            {
                return false;
            }

            float minPos, maxPos;
            GenerateMotionPointsFromPattern(_targetAnimationPattern, ref _motionPoints, out minPos, out maxPos);

            if (_includeMidPoints.val && _pluginFreeController.selected &&
                SuperController.singleton.editModeToggle.isOn)
            {
                for (int i = 0; i < _motionPoints.Count; i++)
                {
                    if (i % 2 == 0)
                    {
                        continue;
                    }

                    var boxMatrix = Matrix4x4.TRS(_motionPoints[i].Position, Quaternion.identity,
                        new Vector3(0.1f, 0.1f, 0.1f));

                    Graphics.DrawMesh(_animationAtomController.holdPositionMesh, boxMatrix,
                        _animationAtomController.linkLineMaterial,
                        _animationAtomController.gameObject.layer, null, 0, null, false, false);
                }
            }

            int p0, p1;
            if (GetMotionPointIndices(_patternTime.val, _motionPoints, out p0, out p1))
            {
                if (p0 != _lastPointIndex)
                {
                    float yFactor = Mathf.InverseLerp(minPos, maxPos, GetPositionForPlane(_motionPoints[p1].Position));
                    
                    float timeToNextPoint;
                    if (p1 > p0)
                    {
                        timeToNextPoint = _motionPoints[p1].Time - _patternTime.val;
                    }
                    else
                    {
                        timeToNextPoint = _targetAnimationPattern.GetTotalTime() - _patternTime.val +
                                            _motionPoints[p1].Time;
                    }

                    timeToNextPoint /= Mathf.Max(_patternSpeed.val, 0.001f);

                    float launchFactor = _invertPosition.val ? 1.0f - yFactor : yFactor;
                    
                    float launchPos = Mathf.Lerp(_minPosition.val, _maxPosition.val, launchFactor);
                    float launchSpeed = LaunchUtils.PredictMoveSpeed(_lastLaunchPos, launchPos, timeToNextPoint);
                    
                    outPos = (byte) launchPos;
                    outSpeed = (byte) launchSpeed;
                    
                    _lastPointIndex = p0;
                    _lastLaunchPos = launchPos;

                    return true;
                }
            }
            
            return false;
        }

        private float GetMaxPosition(float currentMax, Vector3 pos)
        {
            float val = GetPositionForPlane(pos);
            return val > currentMax ? val : currentMax;
        }
        
        private float GetMinPosition(float currentMin, Vector3 pos)
        {
            float val = GetPositionForPlane(pos);
            return val < currentMin ? val : currentMin;
        }

        public float GetPositionForPlane(Vector3 pos)
        {
            if (_samplePlaneIndex == 0)
            {
                return pos.x;
            }
            if (_samplePlaneIndex == 1)
            {
                return pos.y;
            }
            if (_samplePlaneIndex == 2)
            {
                return pos.z;
            }

            return 0.0f;
        }

        private void GenerateMotionPointsFromPattern(AnimationPattern pattern, ref List<MotionPoint> points,
            out float outMinY, out float outMaxY)
        {
            outMinY = float.MaxValue;
            outMaxY = float.MinValue;

            points.Clear();
            for (int i = 0; i < pattern.steps.Length; i++)
            {
                MotionPoint point;

                point.Time = pattern.steps[i].timeStep;
                point.Position = pattern.steps[i].point.position;

                if (_useLocalSpace.val)
                {
                    point.Position = pattern.transform.InverseTransformPoint(point.Position);
                }

                points.Add(point);

                outMaxY = GetMaxPosition(outMaxY, point.Position);
                outMinY = GetMinPosition(outMinY, point.Position);

                if (_includeMidPoints.val)
                {
                    point.Time = pattern.steps[i].timeStep +
                                 pattern.steps[(i + 1) % pattern.steps.Length].transitionToTime * 0.5f;
                    point.Position = pattern.GetPositionFromPoint(i, 0.5f);

                    if (_useLocalSpace.val)
                    {
                        point.Position = pattern.transform.InverseTransformPoint(point.Position);
                    }

                    points.Add(point);

                    outMaxY = GetMaxPosition(outMaxY, point.Position);
                    outMinY = GetMinPosition(outMinY, point.Position);
                }
            }
        }

        private bool GetMotionPointIndices(float time, List<MotionPoint> points, out int p0, out int p1)
        {
            if (points.Count <= 1)
            {
                p0 = -1;
                p1 = -1;
                return false;
            }

            for (int i = 0; i < points.Count; i++)
            {
                if (time < points[i].Time)
                {
                    continue;
                }
                
                int nextPoint = (i + 1) % points.Count;
                if (nextPoint == 0 || time < points[nextPoint].Time)
                {
                    p0 = i;
                    p1 = nextPoint;
                    return true;
                }
            }

            p0 = -1;
            p1 = -1;
            return false;
        }
        
        public void OnSimulatorUpdate(float prevPos, float newPos, float deltaTime)
        {
            
        }

        public void OnDestroy(VAMLaunch plugin)
        {
            DestroyOptionsUI(plugin);
        }
    }
}