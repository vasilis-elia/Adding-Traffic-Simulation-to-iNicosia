using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;

public class CameraAreaRequest : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private CesiumGeoreference georeference;

    [Header("UDP Settings")]
    [SerializeField] private string pythonIP = "127.0.0.1";
    [SerializeField] private int pythonPort = 8002;

    [Header("Data settings")]
    [SerializeField] private double radiusMeters = 500.0;
    [SerializeField] private float sendInterval = 0.25f;

    private UdpClient client;
    private float timer = 0f;

    [Serializable]
    private class CameraPacket
    {
        public double cameraLon;
        public double cameraLat;
        public double radiusMeters;
    }

    void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        client = new UdpClient();
    }

    void Update()
    {
        if (targetCamera == null || georeference == null)
            return;

        timer += Time.deltaTime;

        if (timer < sendInterval)
            return;

        timer = 0f;

        SendCameraPacket();
    }

    private void SendCameraPacket()
    {
        Vector3 unityCameraPos = targetCamera.transform.position;

        // Used for getting Unity position to GPS coordinates.
        double3 earthCenteredEarthFixed =
            georeference.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(
                    unityCameraPos.x,
                    unityCameraPos.y,
                    unityCameraPos.z
                )
            );
        
        // Unity position to GPS coordinates.
        double3 lonLatHeight =
            CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(
                earthCenteredEarthFixed
            );

        // Packet contains camera GPS coordinates and radius around for simulation area.
        CameraPacket packet = new CameraPacket
        {
            cameraLon = lonLatHeight.x,
            cameraLat = lonLatHeight.y,
            radiusMeters = radiusMeters
        };

        string json = JsonUtility.ToJson(packet);
        byte[] data = Encoding.UTF8.GetBytes(json);

        client.Send(data, data.Length, pythonIP, pythonPort);
    }

    void OnApplicationQuit()
    {
        client?.Close();
    }
}