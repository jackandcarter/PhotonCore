# PhotonCore Security Hardening

PhotonCore's network services enforce several limits to mitigate abuse and reduce the blast radius of malformed traffic.

## Network frame handling

* **Maximum frame size:** TCP helpers reject frames larger than 8 KiB. Oversized frames cause the connection to close immediately to prevent uncontrolled memory growth and potential denial of service conditions.
* **Read timeout:** Login reads are bounded by a 10 second timeout so idle connections cannot monopolize server resources.

## Authentication safeguards

* **Credential length clamp:** Login requests clamp usernames to 32 characters and passwords to 64 characters before reaching the database. This avoids excess allocation and guards against attempts to exploit unusually large credentials.
* **Rate limiting:** Failed logins are tracked per `(remote IP, username)` pair. Five failed attempts within 60 seconds trigger a temporary block that returns a generic failure and closes the connection. This slows brute-force attacks while keeping the response opaque to attackers.

These controls improve resiliency against brute-force attempts, malformed frames, and resource exhaustion while keeping the client-visible behavior unchanged for legitimate players.
