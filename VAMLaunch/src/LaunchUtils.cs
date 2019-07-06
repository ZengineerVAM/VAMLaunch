using System;
using UnityEngine;

namespace VAMLaunchPlugin
{
    public static class LaunchUtils
    {
        public const float LAUNCH_MAX_VAL = 99.0f;
        public const float LAUNCH_MIN_SPEED = 10.0f;
        public const float LAUNCH_MAX_SPEED = 90.0f;
        
        // https://github.com/funjack/launchcontrol/blob/master/protocol/funscript/functions.go#L10
        public static float PredictMoveSpeed(float prevPos, float currPos, float durationSecs)
        {
            double durationNanoSecs = durationSecs * 1e9;
            
            double delta = currPos - prevPos;
            double dist = Math.Abs(delta);

            double mil = (durationNanoSecs / 1e6) * 90 / dist;
            double speed = 25000.0 * Math.Pow(mil, -1.05);

            return Mathf.Clamp((float)speed, LAUNCH_MIN_SPEED, LAUNCH_MAX_SPEED);
        }

        // https://github.com/funjack/launchcontrol/blob/master/protocol/funscript/functions.go#L23
        public static float PredictMoveDuration(float dist, float speed)
        {
            if (dist <= 0.0f)
            {
                return 0.0f;
            }

            double mil = Math.Pow(speed / 25000, -0.95);
            double dur = (mil / (90 / dist)) / 1000;
            return (float) dur;
        }

        // https://github.com/funjack/launchcontrol/blob/master/protocol/funscript/functions.go#L34
        public static float PredictDistanceTraveled(float speed, float durationSecs)
        {
            if (speed <= 0.0f)
            {
                return 0.0f;
            }

            double durationNanoSecs = durationSecs * 1e9;
            
            double mil = Math.Pow((double)speed / 25000, -0.95);
            double diff = mil - durationNanoSecs / 1e6;
            double dist = 90 - (diff / mil * 90);

            return (float) dist;
        }
    }
}