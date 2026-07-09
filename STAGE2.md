You are helping develop a mod called **Tankioneers** for the game Stationeers.

The mod adds a buildable aquarium (a glass tank on a cabinet) where players can pump liquids in and out. The aquarium already has working liquid simulation and a custom water shader.

**Current Task: Add fish and basic interior decor.**

**Constraints and Design:**
- We are using a simple approach: everything is hand-placed in the Unity prefab (no dynamic grid for now).
- The aquarium prefab is called `StructureAquarium`.
- Fish should be able to swim in the full height of the tank for now (ignore current water level for movement).
- Later we will add more systems (oxygen, food, death, etc.).

**What you need to create:**

1. **FishManager.cs**  
   This script goes on the root GameObject of the StructureAquarium prefab.  
   - It should have a public int `MaxFish` (default 6-8).  
   - Public List<Transform> `PathPoints` — the developer will place empty GameObjects in the tank as swim targets.  
   - Public List<GameObject> `FishPrefabs` — list of fish prefabs.  
   - When the aquarium loads or liquid changes, spawn up to MaxFish. For now just use FishPrefabs[0] for testing.  
   - Add a public method `UpdateWaterLevel(float fillRatio)` so the main aquarium script can tell it the current water percentage.

2. **FishAI.cs**  
   This script is added to each fish prefab.  
   - The fish should pick a random point from PathPoints and swim toward it smoothly.  
   - When it reaches the point, pick a new one.  
   - Add simple obstacle avoidance (raycast forward, steer left/right if hitting something).  
   - Add a gentle swimming animation (up/down bob + rotate to face movement direction).

3. Tell me exactly how to set this up in Unity:
   - What components go on what objects.
   - How the hierarchy should look.
   - What needs to be assigned in the inspector.

4. Show the necessary code changes in the existing `StructureAquarium.cs` file so it talks to the FishManager.

Write clean, well-commented code and explain every step. Ask me questions if anything is unclear about the existing code structure.