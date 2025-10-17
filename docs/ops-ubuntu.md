# PhotonCore Ubuntu Operations Guide

This guide explains how to build, deploy, and manage the PhotonCore services on an Ubuntu VPS using `systemd`.

## Prerequisites

* Ubuntu 22.04 or later with sudo access.
* [.NET SDK 10.0](https://dotnet.microsoft.com/) installed on your server or build workstation.
* `systemd` (installed by default on Ubuntu) and `curl` for validation.

## Build and publish the application

1. Clone or update the PhotonCore repository on your server (or a build workstation with SSH access to the server):

   ```bash
   git clone https://github.com/PhotonCore/PhotonCore.git
   cd PhotonCore
   ```

2. Publish and stage the binaries under `/opt/photoncore`:

   ```bash
   ./deploy/publish.sh
   ```

   To deploy to a different root (for example `/srv/photoncore`) or restart the services automatically after publishing, use the flags shown in `./deploy/publish.sh --help`.

## Install the services

1. Copy the sample environment files and edit them to match your environment:

   ```bash
   sudo cp deploy/systemd/photoncore-login.env /etc/default/photoncore-login
   sudo cp deploy/systemd/photoncore-admin.env /etc/default/photoncore-admin
   sudo cp deploy/systemd/photoncore-ship.env /etc/default/photoncore-ship
   sudoedit /etc/default/photoncore-login
   sudoedit /etc/default/photoncore-admin
   sudoedit /etc/default/photoncore-ship
   ```

2. Copy the provided `systemd` unit files from `deploy/systemd/` into `/etc/systemd/system/`:

   ```bash
   sudo cp deploy/systemd/photoncore-*.service /etc/systemd/system/
   ```

3. Reload `systemd` to pick up the new units and start the services:

   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable --now photoncore-admin.service photoncore-login.service photoncore-ship.service
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

Logs are written to the journal and can be inspected with `journalctl -u photoncore-<service>.service`. For routine updates, rerun `./deploy/publish.sh --restart` after pulling the latest code.
