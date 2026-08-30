# Begleiter

Der BookDB-Begleiter ermöglicht es Ihnen, Bücher von einem **Telefon oder Tablet** im selben lokalen Netzwerk zu katalogisieren. Sie scannen einen ISBN-Barcode (und fotografieren optional das Cover) auf dem Gerät, und das Buch fließt in die Bibliothek auf diesem Computer. Nichts verlässt Ihr Netzwerk: Das Gerät spricht direkt über Ihr WLAN mit BookDB, niemals über das Internet oder einen externen Server.

Der Begleiter ist **standardmäßig ausgeschaltet**. Sie schalten ihn ein, koppeln jedes Gerät einmal, und von da an verbindet sich das Gerät von selbst wieder, sobald beide im selben Netzwerk sind.

## Den Begleiter einschalten

Öffnen Sie **Extras › Einstellungen › Begleiter** und aktivieren Sie **Begleiter aktivieren**. Die Einstellung wird wirksam, wenn Sie auf **Speichern** drücken — nicht im Moment des Ankreuzens — sodass Sie zuerst den Port und die Aufnahmeoptionen anpassen und alles auf einmal übernehmen können.

Sobald er läuft, zeigt die **Status**-Zeile, dass der Begleiter lauscht. Kann er nicht starten — meist weil der Port bereits belegt ist —, bleibt der Dialog auf dem Reiter Begleiter geöffnet und nennt den Grund, und die Einstellung bleibt aktiviert, sodass Sie den Port ändern und es erneut versuchen können.

Sie können den Begleiter zwischen Sitzungen aktiviert lassen; er startet automatisch mit BookDB, solange die Einstellung eingeschaltet ist.

### Einstellungen

- **Port** — der Netzwerkport, auf dem der Begleiter lauscht (Standard **7443**). Ändern Sie ihn nur, wenn ein anderes Programm diesen Port bereits verwendet. Wenn Sie ihn ändern, ist keine erneute Kopplung nötig, aber das Gerät braucht möglicherweise einen Moment, um BookDB am neuen Port wiederzufinden.
- **Maximale Bildgröße** und **JPEG-Qualität** — wie Cover- und Seitenfotos vor dem Speichern skaliert und komprimiert werden. Niedrigere Werte sparen Platz; höhere Werte behalten mehr Details. Dies gilt für auf dem Gerät aufgenommene Fotos.

## Ein Gerät koppeln

Ein Gerät muss einmal gekoppelt werden, bevor es Bücher senden kann. Beim Koppeln wird ein Satz Sicherheitszertifikate ausgetauscht, sodass nur Geräte, die **Sie** genehmigt haben, eine Verbindung herstellen können, und alles zwischen ihnen verschlüsselt ist.

1. Stellen Sie sicher, dass der Begleiter aktiviert ist und läuft und dass das Gerät im **selben WLAN-Netzwerk** wie dieser Computer ist.
2. Öffnen Sie **Extras › Wartung › Geräte** und drücken Sie **Kopplungscode anzeigen…**. (Auf dem Reiter Einstellungen ▸ Begleiter gibt es außerdem eine Schaltfläche zum Verwalten der Geräte, die dieselbe Stelle öffnet.)
3. Ein Fenster zeigt einen **QR-Code**. Wählen Sie in der BookDB-App auf dem Gerät die Kopplung und scannen Sie den Code.
4. Wenn sich das Gerät verbindet, bestätigt das Fenster die Kopplung. Sie können ein weiteres Gerät koppeln oder das Fenster schließen.

Der Kopplungscode ist **einmalig verwendbar und kurzlebig** — er erneuert sich etwa alle zwei Minuten selbst und kann nur einmal eingelöst werden. Läuft ein Code ab, bevor Sie ihn scannen, erscheint automatisch ein neuer. Ist die automatisch gewählte Netzwerkadresse nicht diejenige, die das Gerät erreichen kann, wählen Sie vor dem Scannen eine andere aus der Auswahlliste.

Sie können bis zu **fünf Geräte** koppeln. Jedes erscheint in der Geräteliste mit dem ihm gegebenen Namen, dem Kopplungszeitpunkt und der letzten Nutzung. Entfernen Sie ein Gerät mit **Entfernen**, um seinen Platz freizugeben; ein entferntes Gerät muss erneut gekoppelt werden, bevor es sich wieder verbinden kann.

## Die Firewall

Beim ersten Start des Begleiters fragen Windows und macOS, ob BookDB eingehende Verbindungen annehmen darf. Sie müssen dies erlauben, sonst können Geräte BookDB nicht erreichen. **Unter Linux fragt nichts nach** — läuft eine Firewall, müssen Sie die Ports selbst freigeben.

- **Windows** — erlauben Sie BookDB in **privaten Netzwerken** (Ihr Heim- oder Büronetzwerk). Sie müssen es in öffentlichen Netzwerken **nicht** erlauben. Haben Sie die Abfrage verworfen oder auf *Abbrechen* geklickt, kommen keine Verbindungen durch; erlauben Sie es erneut unter **Windows-Sicherheit › Firewall- & Netzwerkschutz › Eine App durch die Firewall zulassen**, aktivieren Sie das Kästchen **Privat** für BookDB, oder löschen Sie BookDBs Blockierregel, damit die Abfrage beim nächsten Mal wieder erscheint.
- **macOS** — erlauben Sie eingehende Verbindungen für BookDB, wenn Sie gefragt werden. Sie können dies später unter **Systemeinstellungen › Netzwerk › Firewall** überprüfen.
- **Linux** — es erscheint keine Abfrage. Laufen `ufw` oder `firewalld`, werden eingehende Ports standardmäßig blockiert, und der Begleiter-Port (standardmäßig **7443**) muss für **TCP und UDP** offen sein: TCP trägt die Verbindung, und über UDP findet ein Gerät diesen Computer wieder, wenn sich dessen Adresse ändert. Wird nur TCP geöffnet, funktionieren Kopplung und Durchsuchen, während die Wiederentdeckung stillschweigend fehlschlägt — meist erst viel später bemerkt, als sporadischer Fehler nach einer Adressänderung. Für `ufw`: `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` + `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto udp`, wobei Sie `192.168.1.0/24` durch Ihr eigenes Netzwerk ersetzen. Für `firewalld`: `sudo firewall-cmd --permanent --add-port=7443/tcp --add-port=7443/udp` + `sudo firewall-cmd --reload`. Haben Sie den Begleiter-Port geändert, verwenden Sie in beiden Befehlen Ihren eigenen Port.

Der Begleiter lauscht immer nur in Ihrem lokalen Netzwerk und nur, solange er aktiviert ist.

## Wenn ein Gerät keine Verbindung herstellen kann

- **Beide im selben Netzwerk?** Das Gerät und dieser Computer müssen im selben WLAN sein. Ein „Gast“-Netzwerk ist meist vom Hauptnetzwerk isoliert und funktioniert nicht.
- **Firewall** — prüfen Sie die Firewall-Hinweise oben; ein blockierter Port ist die häufigste Ursache.
- **Läuft der Begleiter?** Die Status-Zeile auf dem Reiter Einstellungen ▸ Begleiter muss ihn als laufend anzeigen. Konnte er nicht starten, ändern Sie den Port und speichern Sie erneut.
- **Noch gekoppelt?** Wurde das Gerät aus der Geräteliste entfernt oder ist es lange her, koppeln Sie es mit einem neuen Code erneut.
- **Ruhezustand** — ging dieser Computer in den Ruhezustand, nimmt der Begleiter beim Aufwachen den Betrieb wieder auf; das Gerät verbindet sich von selbst, sobald beide wach und im Netzwerk sind.

## Was das Gerät kann und was nicht

Ein gekoppeltes Gerät kann ISBNs scannen, Cover- und Seitenfotos aufnehmen, sie zur Katalogisierung senden und die Bibliothek durchsuchen, um zu prüfen, ob Sie ein Buch bereits besitzen. Es arbeitet mit der Bibliothek, die gerade in BookDB geöffnet ist — einschließlich einer auf einem Remote-Datenbankserver gespeicherten Bibliothek. Es kann die Einstellungen von BookDB nicht ändern und keine anderen Geräte entfernen; das bleibt auf diesem Computer.
