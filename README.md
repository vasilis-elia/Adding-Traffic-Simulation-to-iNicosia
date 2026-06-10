# Adding Traffic Simulation to the Digital Twin of Nicosia (iNicosia)
Bachelor Thesis Project

## Overview
This project extends the iNicosia digital twin by integrating a traffic simulation using SUMO and visualizing it in Unity with Cesium for Unity. It simulates traffic flow for a selected area of Nicosia and renders it in a geospatial 3D environment.

The implementation process and technical details are described in the accompanying thesis paper.

## Requirements
- Unity 6000.3.11f1 or later  
- Python 3.12 or later  
- SUMO (Simulation of Urban Mobility) 1.26.0  
- Cesium for Unity package  
- Cesium ion account and access token  

## Project Structure
- Simulation files for a specific area of Nicosia are included and required for running the SUMO simulation.
- The Unity Project is included and required for visualizing the simulation on top of the Digital Twin.

## How to Run

### 1. Run the SUMO Simulation
Run the traffic simulation using:

```bash
python sumo_simulation.py
```

### 2. Run the Unity Project

Press Play after opening the Scene in /Scenes
