# Amplify Impostors bridge

The package does not depend on Amplify Impostors. To let the Editor find a baked impostor for a tree prefab automatically,
copy `AmplifyImpostorBridge.cs.txt` to an Editor folder of your project and rename it to `.cs`. It registers
`TreePrototypeOverrideLibrary.impostorFinder`; without it nothing is searched and the impostor is assigned by hand.
