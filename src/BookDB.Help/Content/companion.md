# Companion

The BookDB companion lets you catalogue books from a **phone or tablet** on the same local network. You scan an ISBN barcode (and optionally photograph the cover) on the device, and the book flows into the library running on this computer. Nothing leaves your network: the device talks directly to BookDB over your Wi-Fi, never through the internet or any outside server.

The companion is **off by default**. You turn it on, pair each device once, and from then on the device reconnects on its own whenever both are on the same network.

## Turning the companion on

Open **Tools › Settings › Companion** and tick **Enable companion**. The setting takes effect when you press **Save** — not the moment you tick the box — so you can adjust the port and capture options first and apply them all at once.

Once running, the **Status** line shows that the companion is listening. If it can't start — usually because the port is already in use — the dialog stays open on the Companion tab and tells you why, and the setting stays enabled so you can change the port and try again.

You can leave the companion enabled between sessions; it starts automatically with BookDB whenever the setting is on.

### Settings

- **Port** — the network port the companion listens on (default **7443**). Change it only if another program already uses that port. If you change it, re-pairing is not required, but the device may take a moment to rediscover BookDB at the new port.
- **Largest image size** and **JPEG quality** — how cover and page photos are scaled and compressed before they are stored. Lower values save space; higher values keep more detail. These apply to photos taken on the device.

## Pairing a device

A device must be paired once before it can send books. Pairing exchanges a set of security certificates so that only devices **you** have approved can connect, and everything between them is encrypted.

1. Make sure the companion is enabled and running, and that the device is on the **same Wi-Fi network** as this computer.
2. Open **Tools › Maintenance › Devices** and press **Show pairing code**. (There is also a **Manage devices** button on the Settings ▸ Companion tab that opens the same place.)
3. A window shows a **QR code**. In the BookDB app on the device, choose to pair and scan the code.
4. When the device connects, the window confirms the pairing. You can pair another device or close the window.

The pairing code is **single-use and short-lived** — it refreshes itself every couple of minutes and can only be claimed once. If a code expires before you scan it, a fresh one appears automatically. If the automatically chosen network address isn't the one the device can reach, pick another from the dropdown before scanning.

You can pair up to **five devices**. Each appears in the Devices list with the name it was given, when it was paired, and when it was last used. Remove a device with **Remove** to free its slot; a removed device must be paired again before it can reconnect.

## The firewall

The first time the companion starts, Windows and macOS ask whether to allow BookDB to accept incoming connections. You must allow it, or devices won't be able to reach BookDB. **On Linux nothing asks you** — if a firewall is running, you have to open the ports yourself.

- **Windows** — allow BookDB on **Private networks** (your home or office network). You do **not** need to allow it on Public networks. If you dismissed the prompt or clicked *Cancel*, no connections will get through; re-allow it under **Windows Security › Firewall & network protection › Allow an app through firewall**, tick BookDB's **Private** box, or delete BookDB's blocking rule so the prompt appears again next time.
- **macOS** — allow incoming connections for BookDB when asked. You can review this later under **System Settings › Network › Firewall**.
- **Linux** — nothing will prompt you. If `ufw` or `firewalld` is running it blocks incoming ports by default, and the companion port (**7443** by default) has to be open for **both TCP and UDP**: TCP carries the connection, and UDP is how a device finds this computer again when its address changes. Opening only TCP leaves pairing and browsing working while rediscovery fails silently — usually noticed much later, as an intermittent fault after the address changes. For `ufw`: `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` + `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto udp`, replacing `192.168.1.0/24` with your own network. For `firewalld`: `sudo firewall-cmd --permanent --add-port=7443/tcp --add-port=7443/udp` + `sudo firewall-cmd --reload`. If you changed the companion port, use your own port in both commands.

The companion only ever listens on your local network, and only while it is enabled.

## If a device can't connect

- **Both on the same network?** The device and this computer must be on the same Wi-Fi. A "guest" network is usually isolated from the main one and won't work.
- **Firewall** — check the firewall notes above; a blocked port is the most common cause.
- **Companion running?** The Status line on the Settings ▸ Companion tab must show it as running. If it failed to start, change the port and Save again.
- **Still paired?** If the device was removed from the Devices list, or if it has been a long time, pair it again with a fresh code.
- **Sleep** — if this computer went to sleep, the companion resumes when it wakes; the device reconnects on its own once both are awake and on the network.

## What the device can and can't do

A paired device can scan ISBNs, take cover and page photos, send them to be catalogued, and browse the library to check whether you already own a book. It works on the library that is currently open in BookDB — including a library stored on a remote database server. It cannot change BookDB's settings or remove other devices; that stays on this computer.
