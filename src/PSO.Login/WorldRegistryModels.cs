// SPDX-License-Identifier: Apache-2.0
namespace PSO.Login;

internal sealed record WorldListEnvelope(WorldSummary[] Worlds);

internal sealed record WorldSummary(string Name, string Address, int Port);
