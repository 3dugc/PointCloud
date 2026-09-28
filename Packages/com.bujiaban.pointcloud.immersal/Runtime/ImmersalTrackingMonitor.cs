using System;
using System.Collections.Generic;
using Immersal.XR;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;

namespace Bujiaban.PointCloud.Immersal
{
    /// <summary>
    /// Observes host tracking without owning, moving or resetting its AR session.
    /// A result may only use evidence from the revision in which its frame began.
    /// </summary>
    internal sealed class ImmersalTrackingMonitor : IDisposable
    {
        private readonly ImmersalTrackingState _state;
        private readonly bool _observesArSession;
        private readonly List<XRInputSubsystem> _availableInputs = new List<XRInputSubsystem>();
        private readonly List<XRInputSubsystem> _subscribedInputs = new List<XRInputSubsystem>();
        private XROrigin _origin;
        private bool _observedOrigin;
        private bool _refreshing;
        private bool _disposed;

        internal ImmersalTrackingMonitor(IPlatformSupport platform, Action<string> invalidateEvidence)
        {
            if (platform == null) throw new ArgumentNullException(nameof(platform));
            if (invalidateEvidence == null) throw new ArgumentNullException(nameof(invalidateEvidence));
            _observesArSession = platform is ARFoundationSupport;
            _state = new ImmersalTrackingState(reason =>
            {
                try { invalidateEvidence(reason); }
                catch (Exception exception) { Debug.LogException(exception); }
            }, _observesArSession,
                !_observesArSession || ARSession.state == ARSessionState.SessionTracking);
            if (_observesArSession)
                ARSession.stateChanged += OnSessionStateChanged;
            ObserveEnvironment();
        }

        internal int Revision
        {
            get { ObserveEnvironment(); return _state.Revision; }
        }

        internal bool IsTracking
        {
            get { ObserveEnvironment(); return _state.IsTracking; }
        }

        internal void ObservePlatformStatus(int trackingQuality)
        {
            ObserveEnvironment();
            _state.ObservePlatformStatus(trackingQuality);
        }

        internal bool CanAccept(int frameRevision)
        {
            ObserveEnvironment();
            return _state.CanAccept(frameRevision);
        }

        internal void SetPaused(bool paused) => _state.SetPaused(paused);
        internal void Invalidate(string reason) => _state.Invalidate(reason);

        private void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            _state.ObserveSessionTracking(args.state == ARSessionState.SessionTracking);
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            if (!_disposed)
                _state.Invalidate("tracking_origin_updated");
        }

        private void ObserveEnvironment()
        {
            if (_disposed || _refreshing) return;
            _refreshing = true;
            try
            {
                if (_observesArSession)
                    _state.ObserveSessionTracking(ARSession.state == ARSessionState.SessionTracking);

#if UNITY_2023_2_OR_NEWER
                SubsystemManager.GetSubsystems(_availableInputs);
#else
                SubsystemManager.GetInstances(_availableInputs);
#endif
                for (int i = _subscribedInputs.Count - 1; i >= 0; i--)
                {
                    XRInputSubsystem input = _subscribedInputs[i];
                    if (_availableInputs.Contains(input) && input.running) continue;
                    input.trackingOriginUpdated -= OnTrackingOriginUpdated;
                    _subscribedInputs.RemoveAt(i);
                    _state.Invalidate("xr_input_subsystem_stopped");
                }
                foreach (XRInputSubsystem input in _availableInputs)
                {
                    if (!input.running || _subscribedInputs.Contains(input)) continue;
                    input.trackingOriginUpdated += OnTrackingOriginUpdated;
                    _subscribedInputs.Add(input);
                }

                if (_origin == null || !_origin.isActiveAndEnabled)
                {
                    _origin = Camera.main != null ? Camera.main.GetComponentInParent<XROrigin>() : null;
                    if (_origin == null)
                        _origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
                }
                GameObject originObject = _origin != null ? _origin.Origin : null;
                if (originObject != null)
                {
                    _observedOrigin = true;
                    _state.ObserveOrigin(originObject.GetEntityId(), originObject.transform.localToWorldMatrix);
                }
                else if (_observedOrigin)
                {
                    _state.ObserveOrigin(default, Matrix4x4.identity);
                }
            }
            finally { _refreshing = false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_observesArSession)
                ARSession.stateChanged -= OnSessionStateChanged;
            foreach (XRInputSubsystem input in _subscribedInputs)
                input.trackingOriginUpdated -= OnTrackingOriginUpdated;
            _subscribedInputs.Clear();
            _availableInputs.Clear();
            _state.Dispose();
        }
    }

    // State decisions are independently testable without an AR session or native SDK.
    internal sealed class ImmersalTrackingState : IDisposable
    {
        private readonly Action<string> _invalidateEvidence;
        private readonly bool _requiresSessionTracking;
        private bool _platformTracking;
        private bool _sessionTracking;
        private bool _paused;
        private bool _disposed;
        private bool _notifying;
        private bool _hasOrigin;
        private bool _originValid;
        private EntityId _originId;
        private Matrix4x4 _originMatrix;

        internal ImmersalTrackingState(Action<string> invalidateEvidence,
            bool requiresSessionTracking = false, bool sessionTracking = true)
        {
            _invalidateEvidence = invalidateEvidence ?? throw new ArgumentNullException(nameof(invalidateEvidence));
            _requiresSessionTracking = requiresSessionTracking;
            _sessionTracking = sessionTracking;
        }

        internal int Revision { get; private set; }
        internal bool IsTracking => !_disposed && !_paused && _platformTracking &&
            (!_requiresSessionTracking || _sessionTracking) && (!_hasOrigin || _originValid);

        internal bool CanAccept(int frameRevision) => IsTracking && frameRevision == Revision;

        internal void ObservePlatformStatus(int quality)
        {
            if (_disposed) return;
            bool wasTracking = IsTracking;
            _platformTracking = quality > 0;
            if (wasTracking && !IsTracking) Invalidate("platform_tracking_lost");
        }

        internal void ObserveSessionTracking(bool tracking)
        {
            if (_disposed) return;
            bool wasTracking = IsTracking;
            _sessionTracking = tracking;
            if (wasTracking && !IsTracking) Invalidate("ar_session_tracking_lost");
        }

        internal void SetPaused(bool paused)
        {
            if (_disposed || _paused == paused) return;
            _paused = paused;
            if (paused) Invalidate("application_paused");
        }

        internal void ObserveOrigin(EntityId originId, Matrix4x4 matrix)
        {
            if (_disposed) return;
            bool valid = originId != default && IsFinite(matrix);
            bool changed = _hasOrigin && (originId != _originId || valid != _originValid ||
                (valid && !ApproximatelyEqual(_originMatrix, matrix)));
            if (!_hasOrigin || changed) _originMatrix = matrix;
            _hasOrigin = true;
            _originId = originId;
            _originValid = valid;
            if (changed) Invalidate("tracking_origin_transform_changed");
        }

        internal void Invalidate(string reason)
        {
            if (_disposed || _notifying) return;
            unchecked { Revision++; }
            _notifying = true;
            try { _invalidateEvidence(reason); }
            finally { _notifying = false; }
        }

        private static bool ApproximatelyEqual(Matrix4x4 first, Matrix4x4 second)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(first[i] - second[i]) > .00001f) return false;
            return true;
        }

        private static bool IsFinite(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; i++)
                if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i])) return false;
            return true;
        }

        public void Dispose() { _disposed = true; }
    }
}
