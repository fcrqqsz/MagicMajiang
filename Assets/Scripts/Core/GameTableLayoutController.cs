using UnityEngine;

namespace MahjongGame.Core
{
    [DisallowMultipleComponent]
    public sealed class GameTableLayoutController : MonoBehaviour
    {
        private const int SeatCount = 4;

        [Header("Scene References")]
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private Transform _tableCenter;
        [SerializeField] private Transform[] _handRoots = new Transform[SeatCount];
        [SerializeField] private Transform[] _riverRoots = new Transform[SeatCount];
        [SerializeField] private Transform[] _meldAnchors = new Transform[SeatCount];

        [Header("Physical Layout")]
        [SerializeField] private float _handDistance = 10f;
        [SerializeField] private float _riverDistance = 3.25f;
        [SerializeField] private float _handStep = 0.84f;
        [SerializeField] private float _drawGap = 0.32f;
        [SerializeField] private float _riverColumnStep = 0.93f;
        [SerializeField] private float _riverRowStep = 1.26f;
        [SerializeField] private int _riverColumns = 6;
        [SerializeField] private Vector3 _meldAnchorLocalPosition = new Vector3(6.4f, 0f, 1.3f);

        [Header("Vertical Fit")]
        [SerializeField] private float _tableSurfaceLocalY = -0.26f;
        [SerializeField] private float _handTileMinimumY = -0.627f;
        [SerializeField] private float _handClearance = 0.03f;
        [SerializeField] private float _flatMeldMinimumY = -0.24f;

        [Header("Camera Framing")]
        [SerializeField] private Vector3 _cameraOffset = new Vector3(0f, 15f, -17f);
        [SerializeField] private float _cameraFieldOfView = 50f;
        [SerializeField] private float _cameraLookHeight = -1.5f;

        private void Awake()
        {
            ApplyLayout();
        }

        public void ApplyLayout()
        {
            Transform centerTransform = _tableCenter != null ? _tableCenter : transform;
            Vector3 center = centerTransform.position;
            float handElevation = GameTableLayoutPolicy.GetSurfaceAlignedRootY(
                _tableSurfaceLocalY,
                _handTileMinimumY,
                _handClearance);
            float meldLocalY = GameTableLayoutPolicy.GetSurfaceAlignedChildLocalY(
                handElevation,
                _tableSurfaceLocalY,
                _flatMeldMinimumY,
                0f);

            for (int seatIndex = 0; seatIndex < SeatCount; seatIndex++)
            {
                TableSeatLayout seat = GameTableLayoutPolicy.GetSeatLayout(
                    seatIndex,
                    _handDistance,
                    _riverDistance);

                Transform handRoot = GetAt(_handRoots, seatIndex);
                if (handRoot != null)
                {
                    handRoot.position = center + new Vector3(seat.HandRoot.X, handElevation, seat.HandRoot.Z);
                    handRoot.rotation = Quaternion.Euler(0f, seat.YawDegrees, 0f);

                    MahjongHandViewBase handView = handRoot.GetComponent<MahjongHandViewBase>();
                    if (handView != null)
                    {
                        handView.tileGap = _handStep;
                        handView.drawGap = _drawGap;
                    }
                }

                Transform riverRoot = GetAt(_riverRoots, seatIndex);
                if (riverRoot != null)
                {
                    riverRoot.SetParent(transform, true);
                    riverRoot.position = center + new Vector3(seat.RiverRoot.X, 0f, seat.RiverRoot.Z);
                    riverRoot.rotation = Quaternion.Euler(0f, seat.YawDegrees, 0f);

                    RiverController river = riverRoot.GetComponent<RiverController>();
                    if (river != null)
                    {
                        river.xGap = _riverColumnStep;
                        river.zGap = _riverRowStep;
                        river.tilesPerRow = _riverColumns;
                    }
                }

                Transform meldAnchor = GetAt(_meldAnchors, seatIndex);
                if (meldAnchor != null)
                {
                    meldAnchor.localPosition = new Vector3(
                        _meldAnchorLocalPosition.x,
                        meldLocalY,
                        _meldAnchorLocalPosition.z);
                    meldAnchor.localRotation = Quaternion.identity;
                }
            }

            Camera cameraToFrame = _mainCamera != null ? _mainCamera : Camera.main;
            if (cameraToFrame == null) return;

            cameraToFrame.transform.position = center + _cameraOffset;
            cameraToFrame.transform.LookAt(center + Vector3.up * _cameraLookHeight);
            cameraToFrame.fieldOfView = _cameraFieldOfView;
        }

        private static Transform GetAt(Transform[] values, int index) =>
            values != null && index >= 0 && index < values.Length ? values[index] : null;
    }
}
