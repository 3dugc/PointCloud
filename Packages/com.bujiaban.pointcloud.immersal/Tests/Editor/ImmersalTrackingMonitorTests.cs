using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Bujiaban.PointCloud.Immersal.Tests
{
    public sealed class ImmersalTrackingMonitorTests
    {
        private GameObject _firstOrigin;
        private GameObject _secondOrigin;

        [SetUp]
        public void CreateOrigins()
        {
            _firstOrigin = new GameObject("First origin");
            _secondOrigin = new GameObject("Second origin");
        }

        [TearDown]
        public void DestroyOrigins()
        {
            Object.DestroyImmediate(_firstOrigin);
            Object.DestroyImmediate(_secondOrigin);
        }

        [Test]
        public void TrackingMustBeEstablishedButFirstGoodStatusDoesNotResetEvidence()
        {
            var reasons = new List<string>();
            using (var state = new ImmersalTrackingState(reasons.Add))
            {
                Assert.That(state.IsTracking, Is.False);
                Assert.That(state.CanAccept(0), Is.False);
                state.ObservePlatformStatus(0);
                state.ObservePlatformStatus(1);
                Assert.That(state.CanAccept(0), Is.True);
                Assert.That(reasons, Is.Empty);
            }
        }

        [Test]
        public void LossAndRecoveryRejectInFlightResultAndOnlyInvalidateOncePerLoss()
        {
            var reasons = new List<string>();
            using (var state = new ImmersalTrackingState(reasons.Add))
            {
                state.ObservePlatformStatus(1);
                int frameRevision = state.Revision;
                state.ObservePlatformStatus(0);
                state.ObservePlatformStatus(0);
                Assert.That(state.IsTracking, Is.False);
                state.ObservePlatformStatus(2);
                Assert.That(state.IsTracking, Is.True);
                Assert.That(state.CanAccept(frameRevision), Is.False);
                Assert.That(state.CanAccept(state.Revision), Is.True);
                Assert.That(reasons, Has.Count.EqualTo(1));

                state.ObservePlatformStatus(-1);
                Assert.That(reasons, Has.Count.EqualTo(2));
            }
        }

        [Test]
        public void ArSessionRecoveryDoesNotReviveAResultFromBeforeTrackingLoss()
        {
            var reasons = new List<string>();
            using (var state = new ImmersalTrackingState(reasons.Add, true, false))
            {
                state.ObservePlatformStatus(1);
                Assert.That(state.IsTracking, Is.False);
                state.ObserveSessionTracking(true);
                int frameRevision = state.Revision;
                Assert.That(frameRevision, Is.Zero);
                state.ObserveSessionTracking(false);
                state.ObserveSessionTracking(false);
                state.ObservePlatformStatus(0);
                state.ObserveSessionTracking(true);
                Assert.That(state.IsTracking, Is.False);
                state.ObservePlatformStatus(1);
                Assert.That(state.CanAccept(frameRevision), Is.False);
                Assert.That(state.CanAccept(state.Revision), Is.True);
                Assert.That(reasons, Is.EqualTo(new[] { "ar_session_tracking_lost" }));
            }
        }

        [Test]
        public void NonArFoundationPlatformDoesNotDependOnArSessionState()
        {
            using (var state = new ImmersalTrackingState(_ => { }))
            {
                state.ObservePlatformStatus(1);
                state.ObserveSessionTracking(false);
                Assert.That(state.IsTracking, Is.True);
                Assert.That(state.Revision, Is.Zero);
            }
        }

        [Test]
        public void PauseInvalidatesOnceAndResumeRequiresANewRevision()
        {
            var reasons = new List<string>();
            using (var state = new ImmersalTrackingState(reasons.Add))
            {
                state.ObservePlatformStatus(1);
                int frameRevision = state.Revision;
                state.SetPaused(true);
                state.SetPaused(true);
                Assert.That(state.IsTracking, Is.False);
                state.SetPaused(false);
                Assert.That(state.CanAccept(frameRevision), Is.False);
                Assert.That(state.CanAccept(state.Revision), Is.True);
                Assert.That(reasons, Is.EqualTo(new[] { "application_paused" }));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void OriginTranslationRotationOrScaleInvalidatesPreviousEvidence(int change)
        {
            var reasons = new List<string>();
            using (var state = new ImmersalTrackingState(reasons.Add))
            {
                state.ObserveOrigin(_firstOrigin.GetEntityId(), Matrix4x4.identity);
                state.ObservePlatformStatus(1);
                int frameRevision = state.Revision;
                Matrix4x4 moved = Matrix4x4.TRS(
                    change == 0 ? Vector3.right : Vector3.zero,
                    change == 1 ? Quaternion.Euler(0, 10, 0) : Quaternion.identity,
                    change == 2 ? Vector3.one * 2 : Vector3.one);
                state.ObserveOrigin(_firstOrigin.GetEntityId(), moved);
                state.ObserveOrigin(_firstOrigin.GetEntityId(), moved);
                Assert.That(state.CanAccept(frameRevision), Is.False);
                Assert.That(state.CanAccept(state.Revision), Is.True);
                Assert.That(reasons, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void ReplacingOrRemovingOriginInvalidatesEvenAtIdenticalCoordinates()
        {
            using (var state = new ImmersalTrackingState(_ => { }))
            {
                state.ObservePlatformStatus(1);
                state.ObserveOrigin(_firstOrigin.GetEntityId(), Matrix4x4.identity);
                state.ObserveOrigin(_secondOrigin.GetEntityId(), Matrix4x4.identity);
                Assert.That(state.Revision, Is.EqualTo(1));
                state.ObserveOrigin(default, Matrix4x4.identity);
                Assert.That(state.Revision, Is.EqualTo(2));
                Assert.That(state.IsTracking, Is.False);
                state.ObserveOrigin(default, Matrix4x4.identity);
                Assert.That(state.Revision, Is.EqualTo(2));
                state.ObserveOrigin(_firstOrigin.GetEntityId(), Matrix4x4.identity);
                Assert.That(state.IsTracking, Is.True);
                Assert.That(state.Revision, Is.EqualTo(3));
            }
        }

        [Test]
        public void RecenterEventRejectsEarlierResultEvenWhenTrackingQualityStayedGood()
        {
            using (var state = new ImmersalTrackingState(_ => { }))
            {
                state.ObservePlatformStatus(1);
                int frameRevision = state.Revision;
                state.Invalidate("tracking_origin_updated");
                Assert.That(state.IsTracking, Is.True);
                Assert.That(state.CanAccept(frameRevision), Is.False);
                Assert.That(state.CanAccept(state.Revision), Is.True);
            }
        }

        [Test]
        public void ReentrantCallbackCannotRecursivelyResetEvidence()
        {
            int callbacks = 0;
            ImmersalTrackingState state = null;
            state = new ImmersalTrackingState(_ =>
            {
                callbacks++;
                state.Invalidate("nested");
            });
            using (state)
            {
                state.Invalidate("tracking_origin_updated");
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(state.Revision, Is.EqualTo(1));
            }
        }

        [Test]
        public void DisposedStateRejectsResultsAndNeverCallsBack()
        {
            var reasons = new List<string>();
            var state = new ImmersalTrackingState(reasons.Add);
            state.ObservePlatformStatus(1);
            state.Dispose();
            state.ObservePlatformStatus(0);
            state.ObserveSessionTracking(false);
            state.SetPaused(true);
            state.ObserveOrigin(_secondOrigin.GetEntityId(), Matrix4x4.identity);
            state.Invalidate("ignored");
            Assert.That(state.CanAccept(state.Revision), Is.False);
            Assert.That(reasons, Is.Empty);
        }
    }
}
