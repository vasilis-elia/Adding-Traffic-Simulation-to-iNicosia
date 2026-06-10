import traci
import json
import socket
import time
import random
import threading
import math

STEP_TIME = 0.05

SUMO_BINARY = "sumo-gui"
SUMO_CONFIG = "nicosia.sumocfg"

UNITY_IP = "127.0.0.1"
UNITY_SIMULATION_PORT = 8001
CAMERA_LISTEN_PORT = 8002
PRINT_STEP_NUMBERS = 20

MAX_VEHICLES = 30
MAX_PEDESTRIANS = 1000

SIM_DURATION = 10000

# Creates UDP socket for Unity.
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# In order to not send over data until we know Unity listens and has sent area to send.
first_camera_packet = True

# Unity camera area. This data is retrieved from the packet sent from Unity.
camera_area = {
    "cameraLon": None,
    "cameraLat": None,
    "radiusMeters": 500.0
}

# SUMO simulation starts using traCI.
def start_sumo():
    traci.start([
        SUMO_BINARY,
        "-c", SUMO_CONFIG,
        "--start",
        "--step-length", "0.05", # New simulation step every 0.05s of real time. Matches STEP_TIME to try and have real time match simulation time.
        "--log", "sumo_log.txt",
        "--error-log", "sumo_error.txt",
        "--verbose", "true"
    ])

# Returns all the road edges of the network that vehicles can use.
def get_road_edges():
    edges = traci.edge.getIDList() # Gets all the edge ids. Edge is a road segment between two junctions.
    valid = [] # Holds the valid edges. Valid edge is it has at least one lane and the lane allows vehicles.

    for edge in edges:

        if edge.startswith(":"): # Skips internal junction edges (they start with ':', source: https://sumo.dlr.de/docs/Networks/SUMO_Road_Networks.html ).
            continue

        try:
            # Skips edges that have no lanes.
            lane_count = traci.edge.getLaneNumber(edge)
            if lane_count == 0:
                continue

            allows_vehicles = False

            # Need to check if th
            for i in range(lane_count):
                lane_id = f"{edge}_{i}" # SUMO lane ids are edgeID_0, edgeID_1, etc. Source: https://sumo.dlr.de/docs/Specification/index.html "the lane id takes the form <edge_id>_<lane_index>."
                allowed = traci.lane.getAllowed(lane_id) # Returns allowed vehicles (empty means all types are allowed).

                # Valid edge if it allows all vehicles or passenger ones.
                if allowed is None or "passenger" in allowed:
                    allows_vehicles = True
                    break
        
            if allows_vehicles:
                valid.append(edge)

        except:
            continue

    return valid

# Returns all road / sidewalk edges of the network tha pedestrians can walk on. (Similar to get_road_edges() ).
def get_pedestrian_edges():
    edges = traci.edge.getIDList()
    valid = []

    for edge in edges:
        if edge.startswith(":"):
            continue

        try:
            lane_count = traci.edge.getLaneNumber(edge)

            if lane_count == 0:
                continue

            for i in range(lane_count):
                lane_id = f"{edge}_{i}"
                allowed = traci.lane.getAllowed(lane_id)
                disallowed = traci.lane.getDisallowed(lane_id)             

                # Important to note that some roads (especially in Nincosia) allow pedestrians and cars.
                # If allowed is None all vehicles are allowed: https://sumo.dlr.de/pydoc/traci/_lane.html#LaneDomain.getAllowed
                #if allowed is not None and "pedestrian" in allowed:
                if "passenger" not in allowed and "pedestrian" in allowed and "pedestrian" not in disallowed:
                    valid.append(edge)
                    break
        except:
            continue

    return valid

# Creates a route for vehicles between 2 random edges if a valid route is found.
def create_random_vehicle_route(route_id, road_edges):

    # Number of tries to find a valid route between random edges.
    for _ in range(30):

        # This sometimes will cause warnings, since the network is not strongly connected.
        start = random.choice(road_edges)  
        end = random.choice(road_edges)

        # Skip route if it's only 1 edge.
        if start == end:
            continue

        try:
            # SUMO returns a valid route between the two edges, if one exists.
            route = traci.simulation.findRoute(start, end)

            # Skip this pair of edges if no route is found.
            if not route or not route.edges:
                continue
      
            valid = True

            # Checks whether all the edges in the route SUMO has returned have at least one lane.
            for edge in route.edges:
                try:
                    if traci.edge.getLaneNumber(edge) == 0:
                        valid = False
                        break
                except:
                    valid = False
                    break

            if not valid:
                continue
         
            # Adds valid route to SUMO simulation.
            traci.route.add(route_id, route.edges)
            return True

        except traci.TraCIException:
            continue

    return False

# Spawns a pedestrian on a random route.
def spawn_pedestrian(ped_edges, ped_id):

    for _ in range(10):        
        pid = f"ped_{ped_id}"    

        start = random.choice(ped_edges)
        end = random.choice(ped_edges)

        if start == end:
            continue

        try:
            # For pedestrians findInternalRoute is recommended. https://sumo.dlr.de/docs/TraCI/Simulation_Value_Retrieval.html
            # "Returns a tuple of Stage objects that correspond to the sequence of walks and rides to reach the destination."
            stages = traci.simulation.findIntermodalRoute(
                start,
                end,                
            )

            # Skip if no available route / stage.
            if not stages:
                continue

            walking_edges = []

            # Makes sure each stage has stages.
            for stage in stages:
                if stage.edges:
                    walking_edges.extend(stage.edges)

            if not walking_edges:
                continue
         
            # Adds the person to the route at position 0.
            traci.person.add(pid, edgeID=start, pos=0)

            # Random walking speed for pedestrians.
            walk_speed = random.uniform(1.0, 1.5) 

            # Pedestrian starts walking on the designated route with random walk speed. https://sumo.dlr.de/pydoc/traci/_person.html#PersonDomain.appendWalkingStage
            traci.person.appendWalkingStage(
                personID=pid,
                edges=walking_edges,
                arrivalPos=0.0, # Arrivan position at last edge.
                speed=walk_speed)            
            return True

        except traci.TraCIException:
            continue

    return False

# Uses Haversine formula to calculate distance between 2 GPS locations (Camera's and agent's).
def get_distance_to_meters(lon1, lat1, lon2, lat2):
    
    radius_earth = 6371000.0

    lon1_rad = math.radians(lon1)
    lat1_rad = math.radians(lat1)
    lon2_rad = math.radians(lon2)
    lat2_rad = math.radians(lat2)

    dlon = lon2_rad - lon1_rad
    dlat = lat2_rad - lat1_rad

    # Havernsine formula.
    a = (math.sin(dlat / 2) ** 2
        + math.cos(lat1_rad) * math.cos(lat2_rad) * math.sin(dlon / 2) ** 2)

    c = 2 * math.atan2(math.sqrt(a), math.sqrt(1 - a))

    # Distance in meters.
    return radius_earth * c

# Returns true if specified GPS coordinates are inside the camera radius.
def is_inside_camera_radius(lon, lat):
    camera_lon = camera_area["cameraLon"]
    camera_lat = camera_area["cameraLat"]
    radius_meters = camera_area["radiusMeters"]

    # If Unity has not sent camera data yet, send nothing.
    if camera_lon is None or camera_lat is None:
        return False

    # Distance in meters between camera and agent.
    distance = get_distance_to_meters(camera_lon, camera_lat, lon, lat)

    # Agent is within camera radius if their distance is smaller than the radius.
    return distance <= radius_meters

# Collects data of vehicles that are inside the camera radius.
def collect_vehicle_data():
    vehicles = traci.vehicle.getIDList()
    data = []

    for vehicle in vehicles:
        x, y = traci.vehicle.getPosition(vehicle)    # Position in meters.
        lon, lat = traci.simulation.convertGeo(x, y) # Position in GPS coordinates.

        # Filters out the vehicles not in the camera radius.
        if not is_inside_camera_radius(lon, lat):
            continue

        speed = traci.vehicle.getSpeed(vehicle)
        angle = traci.vehicle.getAngle(vehicle)

        # Adds the current vehicle to the total vehicle data to send to Unity.
        data.append({
            "id": vehicle,
          
            "lon": lon,
            "lat": lat,          

            "speed": speed,
            "angle": angle,
            "type": "vehicle"
        })

    return data

# Collects data of pedestrians that are inside the camera radius. Similar to collect_vehicle_data().
def collect_pedestrian_data():
    pedestrians = traci.person.getIDList()
    data = []

    for pedestrian in pedestrians:
        x, y = traci.person.getPosition(pedestrian)
        lon, lat = traci.simulation.convertGeo(x, y)

        if not is_inside_camera_radius(lon, lat):
            continue

        speed = traci.person.getSpeed(pedestrian)
        angle = traci.person.getAngle(pedestrian)

        data.append({
            "id": pedestrian,     
         
            "lon": lon,
            "lat": lat,         

            "speed": speed,
            "angle": angle,
            "type": "pedestrian"
        })

    return data

# Sends packet to Unity in JSON format.
def send_to_unity(data):
    send_start = time.perf_counter()

    message = json.dumps(data)
    encoded = message.encode("utf-8")
    sock.sendto(encoded, (UNITY_IP, UNITY_SIMULATION_PORT))

    send_time = time.perf_counter() - send_start

    return send_time, len(encoded)

# Listens for Unity camera coordinates. Does not send packets until first Unity packet is received.
def listen_for_unity_camera():
    global camera_area, first_camera_packet

    # Creates UDP socket and listen to specific port for Unity packet.
    camera_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    camera_sock.bind((UNITY_IP, CAMERA_LISTEN_PORT))

    print(f"No Unity packet received yet. Port: {CAMERA_LISTEN_PORT}")

    while True:
        try:
            # Listens for packet from Unity.
            data, addr = camera_sock.recvfrom(4096)
            message = data.decode("utf-8")
            packet = json.loads(message)

            # Unity packet conatins camera GPS coordinates and radius around camera.
            camera_area["cameraLon"] = packet.get("cameraLon")
            camera_area["cameraLat"] = packet.get("cameraLat")
            camera_area["radiusMeters"] = packet.get("radiusMeters", 500.0)

            if first_camera_packet:
                print("Unity packet received. Sending packets to Unity now.")
                first_camera_packet = False

        except Exception as e:
            print("Unity camera error:", e)
            
# Main simulation loop.
def run():

    # Creates a separate thread for listen to Unity camera packets.
    camera_thread = threading.Thread(
        target=listen_for_unity_camera,
        daemon=True)

    camera_thread.start()

    start_sumo()

    # One step until TraCI can initialize the network.
    traci.simulationStep()

    # Gets edges from network in SUMO using TraCI.
    road_edges = get_road_edges()
    ped_edges = get_pedestrian_edges()

    print("Valid edges loaded:", len(road_edges))

    step = 0
    veh_id = 0
    ped_id = 0

    # Simulation runs for SIM_DURATION steps.
    while step < SIM_DURATION:

        traci.simulationStep() # Advance one simulation step.

        # Active agents so they can be limited to defined max values.
        active_vehicles = len(traci.vehicle.getIDList())
        active_pedestrians = len(traci.person.getIDList())

        # Creates a vehicle if not at max capacity.
        if active_vehicles < MAX_VEHICLES:

            full_veh_id = f"veh{veh_id}"
            route_id = f"route_{veh_id}"

            route = create_random_vehicle_route(route_id, road_edges)

            if route:
                try:
                    traci.vehicle.add(
                        full_veh_id,
                        routeID=route_id,
                        departLane="best",
                        departSpeed="max")
                    
                    veh_id += 1
                except traci.TraCIException as e:
                    print("Vehicle spawn error:", e)

        if active_pedestrians < MAX_PEDESTRIANS:
            if spawn_pedestrian(ped_edges, ped_id):
                ped_id += 1

        # After Unity sends at least one packet, start sending simulation packets to Unity.
        if has_camera_packet_sent():
            packet_start = time.perf_counter()

            vehicles = collect_vehicle_data()
            pedestrians = collect_pedestrian_data()

            packet = {
                "vehicles": vehicles,
                "pedestrians": pedestrians
            }

            packet_ready_time = time.perf_counter() - packet_start

            send_time, packet_size = send_to_unity(packet)

            # Print every few steps.
            if step % PRINT_STEP_NUMBERS == 0:
                print(
                    f"packet_ready={packet_ready_time * 1000:.2f} ms | "
                    f"send={send_time * 1000:.2f} ms | "
                    f"size={packet_size} bytes | "
                    f"sent={len(vehicles) + len(pedestrians)}"
                )
        else:
            print("Waiting for Unity first packet")

        #print(f"Step: {step} | Vehicles: {active_vehicles} | Pedestrians: {active_pedestrians}")

        step += 1
        time.sleep(STEP_TIME) # Time before advancing to the next simulation step.

    traci.close()

# Helper function to know whether Unity has sent at least one packet.
def has_camera_packet_sent():
    return (camera_area["cameraLon"] is not None
            and camera_area["cameraLat"] is not None)

if __name__ == "__main__":
    run()