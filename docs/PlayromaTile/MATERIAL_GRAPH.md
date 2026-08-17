# Material Graph Architecture

## Typed values

Nodes exchange one of these data families:

- colour;
- scalar;
- vector2;
- mask;
- integer;
- palette;
- curve.

Scalar fields are reused for height, roughness, wear, moisture, flow components, masks, and probabilities.

## Evaluation context

Every evaluation supplies:

- dimensions;
- tile dimensions;
- seed;
- frame index and frame count;
- wrap modes;
- draft, interactive, or final quality;
- cancellation token;
- progress sink;
- portable asset resolver.

Nodes must not depend on hidden global random state.

## Graph format

Each node stores:

- stable GUID;
- node type identifier;
- node implementation version;
- editor position;
- parameters;
- named input connections.

The recipe stores the output node GUID and exposed Quick Generator controls.

## Evaluation

The evaluator begins at the output node, recursively evaluates dependencies, and caches each node result for the duration of the render. Independent candidate renders run in parallel.

A future implementation may add a persistent cross-render cache without changing the recipe format.

## Compatibility

Unknown nodes retain their identifiers, parameters, and connections when serialized. The validator prevents rendering until the missing node implementation or plugin is restored.

## Tileability

Node definitions advertise whether tileability is:

- natively periodic;
- preserved;
- absent;
- repaired by the operation.

The validator warns when non-periodic operations are not followed by a seam-repair stage.
