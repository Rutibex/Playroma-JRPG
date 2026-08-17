# Originality and asset policy

Playroma Sound is intended to create original procedural audio. The repository does not include commercial sample packs, extracted game audio, ROM data, proprietary impulse responses, or copied species/sound parameter tables.

## Contributions

Contributed code, presets, examples, and assets must be one of the following:

- created by the contributor and licensed for inclusion under the repository's MIT License;
- derived from public-domain material;
- included under a compatible license with the required attribution and notice;
- a procedural description that does not reproduce protected source data.

Do not submit audio ripped from games, films, music releases, commercial libraries, or websites. Do not recreate a copyrighted sound by copying extracted command tables or hidden parameters from a commercial work.

## Hardware-inspired synthesis

It is acceptable to model documented hardware behaviour, common synthesis techniques, acoustic principles, and general genre characteristics. Hardware-inspired generators should create new parameter sequences rather than ship extracted content.

The classic handheld cry generator uses fixed-duty pulse channels, four-bit envelopes, period-based pitch, and LFSR noise as general synthesis primitives. Its phrase programs are generated procedurally and do not contain commercial cry tables.

## Trademarks

Names of third-party products may appear in technical discussion only to explain interoperability or historical inspiration. They do not imply sponsorship, endorsement, or affiliation.

## Third-party notices

The Windows playback adapter references NAudio.Wasapi, an MIT-licensed dependency. See `THIRD_PARTY_NOTICES.md`.
