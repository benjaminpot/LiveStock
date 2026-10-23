# LiveStock Multiplayer stage 4

## Roles

Select the role in Menu before connecting. PC Observer starts the host; VR Sheep joins that host as a client. Start the PC observer first. For local development without Photon use Offline test with the selected role.

PC Observer: WASD flies forward/back/sideways, Q/E descends/ascends, hold right mouse to look, hold left Shift to accelerate. It has no visible Capsule and is not bounded to the test platform.

VR Sheep: the local rig instantiates `Assets/_Project/model/VR_sheep_player.prefab`. The original asset is preserved. Recording autoplay is disabled on the runtime instance, and head/hand anchors drive the existing sheep IK targets. A separate `RemoteSheepVisual.prefab` retains the visual/IK components and receives the three target poses through Fusion; it has no hardware tracking scripts, cameras or AudioListeners. Exact headset calibration and remote sheep appearance require a real VR plus PC test.

Open `Assets/_Project/Scenes/Menu.unity` and enter Play mode.
Use **Offline test** to start a Fusion Single session without a Photon account.
Use WASD or arrow keys to move the Capsule on the test platform.
Use **Start Farm (Host)** to load the multiplayer farm copy.
Use **Leave** to shut down the runner and return to Menu.

For online tests, fill **Fusion > Realtime Settings > App Id Fusion** with your Fusion 2 application ID. All clients must use the same App ID, region, app version and room name (`LIVESTOCK-DEV`). Build and launch two copies to test two players. Maximum players: 4. The first client becomes the host. Host migration is not implemented; when the host disconnects clients return to Menu.

Scene order: Menu, Lobby, Farm_Multiplayer. Existing build scenes remain after these entries. The farm copy includes a flat prototype platform near the origin; the capsule controller is a bounded horizontal prototype and does not yet implement terrain walking, physics, VR tracking, or sheep animation.

`Tools > LiveStock > Setup Multiplayer Stage 4` can restore missing prototype assets. Save open scenes before running it. It preserves existing scene content and reuses an existing player prefab.

Fusion configuration must weave `Meta.XR.MultiplayerBlocks.Fusion` in addition to Assembly-CSharp because the installed Meta XR SDK declares network structs in that assembly.
