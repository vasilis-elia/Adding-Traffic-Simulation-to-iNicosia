using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class SumoReceiver : MonoBehaviour
{
    UdpClient client;
    int port = 8001;

    private Simulator trafficSimulator;

    Packet latestPacket;
    bool packetReady = false;


    void Start()
    {
        // Initializes UDP client to listen for simulation data.
        client = new UdpClient(port);
        client.BeginReceive(ReceiveData, null);

        trafficSimulator = GetComponent<Simulator>();
    }

    // Called when packets are received.
    void ReceiveData(IAsyncResult result)
    {
        IPEndPoint senderIp = new IPEndPoint(IPAddress.Any, port);
        byte[] data = client.EndReceive(result, ref senderIp);

        string message = Encoding.UTF8.GetString(data);

        latestPacket = JsonUtility.FromJson<Packet>(message); // Updates latest packet with received simulation data.
        packetReady = true;

        client.BeginReceive(ReceiveData, null);
    }

    void Update()
    {
        // Sends simulation data to simulator, to render everything.
        if (packetReady)
        {
            trafficSimulator.UpdateVehicles(latestPacket.vehicles);
            trafficSimulator.UpdatePedestrians(latestPacket.pedestrians);
            packetReady = false;
        }
    }

    void OnApplicationQuit()
    {
        client.Close();
    }
}

// Vehicle data to receive.
[System.Serializable]
public class Vehicle
{
    public string id;
    public double lon;
    public double lat;
    public double height;
    public float speed;
    public float angle;
}

// Pedestrian data to receive.
[System.Serializable]
public class Pedestrian
{
    public string id;
    public double lon;
    public double lat;
    public double height;
    public float speed;
    public float angle;
}

[System.Serializable]
public class Packet
{
    public int step;
    public Vehicle[] vehicles;
    public Pedestrian[] pedestrians;
}