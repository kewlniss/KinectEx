﻿using System.Collections.Generic;

#if NOSDK
using KinectEx.KinectSDK;
#elif NETFX_CORE
using WindowsPreview.Kinect;
#else
using Microsoft.Kinect;
#endif

#if NETFX_CORE
using Windows.Foundation;
#endif

namespace KinectEx
{
    public interface IBody
    {
        IReadOnlyDictionary<Activity, DetectionResult> Activities { get;  }
        IReadOnlyDictionary<Appearance, DetectionResult> Appearance { get; }
        FrameEdges ClippedEdges { get; set; }
        DetectionResult Engaged { get; set; }
        IReadOnlyDictionary<Expression, DetectionResult> Expressions { get; }
        TrackingConfidence HandLeftConfidence { get; set; }
        HandState HandLeftState { get; set; }
        TrackingConfidence HandRightConfidence { get; set; }
        HandState HandRightState { get; set; }
        bool IsRestricted { get; set; }
        bool IsTracked { get; set; }
        IReadOnlyDictionary<JointType, JointOrientation> JointOrientations { get; }
        IReadOnlyDictionary<JointType, IJoint> Joints { get; set; }
#if NETFX_CORE
        Point Lean { get; set; }
#else
        PointF Lean { get; set; }
#endif
        TrackingState LeanTrackingState { get; set; }
        ulong TrackingId { get; set; }

        void Update(IBody body);
#if !NOSDK
        void Update(Body body);
#endif
    }
}
