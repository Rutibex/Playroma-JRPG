# Streaming scene examples

These `.playstream` files demonstrate the scene format. Their `sounds` entries are logical graph IDs; provide matching `.playsound`, `.playgen`, or in-memory graph definitions through an `ISoundAssetResolver` before compiling them.

The executable streaming demo uses the built-in scene generators, which return both a scene and the corresponding in-memory graph assets.


`authoring-preview.playstream` is indexed by `../PlayromaSoundExamples.playproject` and is used by the authoring demonstration. Its graph IDs resolve through the workspace, including unsaved open graph or generator documents.
