# File formats

Playroma Sound uses JSON-based, versioned, human-readable files. The authoritative schemas are stored in `schemas/`.

## `.playproject`

A project is an asset index. It records a stable project ID, display name, relative asset paths, asset kinds, tags, sample directories, and export profiles.

Schema: `schemas/playproject.schema.json`

## `.playgen`

A generator request stores a generator ID, semantic description, deterministic seeds, variation index, quality mode, and identity metadata. The description payload is generator-specific and is validated by the generator catalog and description type.

Generator requests can be regenerated, mutated, rendered, or converted into ordinary graphs.

Schema: `schemas/playgen.schema.json`

## `.playsound`

A sound graph contains:

- format version and graph identity;
- root seed and metadata;
- node definitions and serialized parameters;
- typed connections;
- macros;
- an explicit output binding.

Schema: `schemas/playsound.schema.json`

The current graph format version is defined by the graph serializer. Migrations are applied when supported rather than requiring every caller to rewrite older files manually.

## `.playstream`

A streaming scene can contain sounds, buses, acoustic zones, portals, parameters, bindings, layers, stochastic emitters, event clusters, and state machines.

Schema: `schemas/playstream.schema.json`

Scene sound entries use logical asset IDs. A compiler therefore needs an asset resolver in addition to the scene definition.

## `.playexport`

An export profile describes rendered-audio or runtime-package export. It includes output format, sample rate, channels, block size, selected assets, variation count, scene duration, stems, source inclusion, and metadata.

Schema: `schemas/playexport.schema.json`

## Versioning policy

Files include a numeric `formatVersion`. New optional fields should preserve older readers where possible. Breaking structural changes require a new format version and a migration when practical.

Do not use the application release number as a file-format version. Each format evolves independently.

## Schema identifiers

Schema `$id` values are identifiers, not a guarantee that the files are hosted at those addresses. Consumers should use the schema copies distributed with the repository unless a future release publishes an official schema endpoint.
