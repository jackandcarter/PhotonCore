// SPDX-License-Identifier: Apache-2.0
# PC v2 Login Handshake (Clean-Room Summary)

This document summarizes how we interoperate with the Phantasy Star Online PC
Version 2 login handshake. Packet details were inferred by watching public
community servers and by inspecting layout descriptions in our vendored
`third_party/newserv` reference implementation. No names or code were copied;
the shapes below are restated in our own words.

## Transport framing

* Each command is framed with the "PC" header described by the community: a
  2-byte little-endian size (covering the header + payload) followed by an
  8-bit command identifier and an 8-bit flag. The login flow uses flag = 0.
* All multi-byte fields inside the payload follow the conventions observed in
  PC v2 captures: integers are little-endian unless stated otherwise. IPv4
  addresses are encoded in network byte order. Strings are ASCII with
  zero-padding.

## Client hello (command `0x93`)

The login client sends a `0xB0`-byte frame. After the 4-byte header the payload
is laid out as:

| Offset | Size | Description |
| ------ | ---- | ----------- |
| `0x00` | 4    | Player tag (ignored; client keeps sending `0x00010000`). |
| `0x04` | 4    | Guild card number (ignored by our login tier). |
| `0x08` | 8    | Hardware identifier (big-endian; not needed for DB login). |
| `0x10` | 4    | Sub-version flags. |
| `0x14` | 1    | Extended-login marker (0 or 1). |
| `0x15` | 1    | Language code. |
| `0x16` | 2    | Reserved padding. |
| `0x18` | 0x11 | Serial number string, ASCII with trailing zeros — we map this to `ClientHello.Username`. |
| `0x29` | 0x11 | Access key string, ASCII with trailing zeros — we map this to `ClientHello.Password`. |
| `0x3A` | ...  | Remaining license/account strings we currently ignore. |

We only need the serial and access key fields to authenticate against MySQL.

Example frames (whitespace added for readability):

* `alice` / `hunter2` – `B0 00 93 00 ... 61 6C 69 63 65 00 ... 68 75 6E 74 65 72 32 00 ...`
* `bob` / `secret` – `B0 00 93 00 ... 62 6F 62 00 ... 73 65 63 72 65 74 00 ...`

## Auth response (command `0xA1`)

We reply with a compact acknowledgement that mirrors what the original server
used. The payload is:

| Offset | Size | Description |
| ------ | ---- | ----------- |
| `0x00` | 1    | Result flag (`1` for success, `0` for failure). |
| `0x01` | 1    | ASCII message length. |
| `0x02` | N    | Message bytes (e.g., `"ok"` or `"invalid"`). |

Constructed examples:

* Success (`"ok"`) – `08 00 A1 00 01 02 6F 6B`
* Failure (`"invalid"`) – `0D 00 A1 00 00 07 69 6E 76 61 6C 69 64`

## World list (command `0xA2`)

After a successful auth we send the ship/world roster. The payload begins with a
count byte followed by fixed-size records:

| Offset | Size | Description |
| ------ | ---- | ----------- |
| `0x00` | 1    | Entry count. |
| `0x01` | ...  | Repeated entries (see below). |

Each entry occupies `0x20 + 4 + 2` bytes:

| Offset | Size | Description |
| ------ | ---- | ----------- |
| `0x00` | 0x20 | World name, ASCII with trailing zeros. |
| `0x20` | 4    | IPv4 address in network byte order. |
| `0x24` | 2    | TCP port in network byte order. |

Examples we hand-built for tests and manual verification:

* Single world (`World-1 @ 127.0.0.1:12001`) –
  `2B 00 A2 00 01 57 6F 72 6C 64 2D 31 00 ... 7F 00 00 01 2E E1`
* Two worlds (`World-1` and `World-2`) –
  `51 00 A2 00 02 57 6F 72 6C 64 2D 31 00 ... 7F 00 00 01 2E E1 57 6F 72 6C 64 2D 32 00 ... C0 A8 00 05 2E E2`

> **Dynamic roster:** The login tier now queries the Admin API's world
> registry before emitting this list. Ships heartbeat with the registry every
> few seconds, and entries expire if they have not checked in within the
> configured TTL (30 seconds by default).

## Test vectors

The unit tests under `tests/PhotonCore.Tests/PcV2CodecTests.cs` reuse the same
vectors to ensure our codec never drifts from the documented layout.
