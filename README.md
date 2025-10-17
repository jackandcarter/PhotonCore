# PhotonCore Context

PhotonCore is a clean-room reimplementation of the server stack for
**Phantasy Star Online (PSO)**. The initial compatibility target is the
**PC Version 2** client (Windows release, protocol compatible with Dreamcast v2).

Later, we will expand to support **Episode I & II (GameCube)** using a parallel
compatibility layer.

The architecture is modular:
- PSO.Proto — defines binary protocol structures and codecs
- PSO.Login — handles authentication and world list responses
- PSO.Ship — handles in-game world/lobby connections
- PSO.Proxy — for packet capture and debugging
- PSO.AdminApi — HTTP API and database access (MySQL)

## PC v2 Compatibility

Our clean-room notes on the PC v2 login handshake, including framing, field
layout, and test vectors, are documented in
[docs/protocol-pc-v2-login.md](docs/protocol-pc-v2-login.md).
