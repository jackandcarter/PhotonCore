# PhotonCore Ubuntu Operations Guide

This guide explains how to build, deploy, and manage the PhotonCore services on an Ubuntu VPS using `systemd`.

## Prerequisites

* Ubuntu 22.04 or later with sudo access.
* [.NET SDK 9.0](https://dotnet.microsoft.com/) installed on your build workstation.
* `systemd` (installed by default on Ubuntu) and `curl` for validation.

## Build the application

1. Clone or update the PhotonCore repository on your build machine.
2. Publish the services in Release mode:

   ```bash
   dotnet publish PhotonCore.sln -c Release
   ```

   Published binaries will be written to `./src/<Project>/bin/Release/net9.0/publish/` for each project.

## Install the services

1. Copy the published outputs for `PSO.AdminApi`, `PSO.Login`, and `PSO.Ship` to the server under `/opt/photoncore`:

   ```bash
   sudo mkdir -p /opt/photoncore
   sudo cp -r src/PSO.AdminApi/bin/Release/net9.0/publish/* /opt/photoncore/
   sudo cp -r src/PSO.Login/bin/Release/net9.0/publish/* /opt/photoncore/
   sudo cp -r src/PSO.Ship/bin/Release/net9.0/publish/* /opt/photoncore/
   ```

2. Copy the provided `systemd` unit files from `deploy/systemd/` into `/etc/systemd/system/`:

   ```bash
   sudo cp deploy/systemd/photoncore-*.service /etc/systemd/system/
   ```

3. Reload `systemd` to pick up the new units:

   ```bash
   sudo systemctl daemon-reload
   ```

4. Enable the services so they start automatically on boot:

   ```bash
   sudo systemctl enable photoncore-admin.service photoncore-login.service photoncore-ship.service
   ```

5. Start the services:

   ```bash
   sudo systemctl start photoncore-admin.service photoncore-login.service photoncore-ship.service
   ```

## Verification

* Confirm that all services are running:

  ```bash
  systemctl status photoncore-admin.service photoncore-login.service photoncore-ship.service
  ```

* Check the Admin API health endpoint:

  ```bash
  curl http://127.0.0.1:5080/healthz
  ```

* Inspect metrics (after enabling observability features):

  ```bash
  curl http://127.0.0.1:5080/metrics
  ```

Logs are written to the journal and can be inspected with `journalctl -u photoncore-<service>.service`.
