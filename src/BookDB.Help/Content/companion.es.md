# Complemento

El complemento de BookDB le permite catalogar libros desde un **teléfono o tableta** en la misma red local. Escanea un código de barras ISBN (y opcionalmente fotografía la portada) en el dispositivo, y el libro fluye a la biblioteca que se ejecuta en este equipo. Nada sale de su red: el dispositivo habla directamente con BookDB por su Wi-Fi, nunca a través de internet ni de ningún servidor externo.

El complemento está **desactivado de forma predeterminada**. Usted lo activa, empareja cada dispositivo una vez, y a partir de entonces el dispositivo se reconecta por sí solo siempre que ambos estén en la misma red.

## Activar el complemento

Abra **Herramientas › Configuración › Complemento** y marque **Activar complemento**. El ajuste surte efecto cuando pulsa **Guardar** —no en el momento de marcar la casilla—, de modo que puede ajustar primero el puerto y las opciones de captura y aplicarlo todo de una vez.

Una vez en marcha, la línea de **Estado** muestra que el complemento está a la escucha. Si no puede iniciarse —normalmente porque el puerto ya está en uso—, el cuadro de diálogo permanece abierto en la pestaña Complemento y le indica el motivo, y el ajuste sigue activado para que pueda cambiar el puerto y volver a intentarlo.

Puede dejar el complemento activado entre sesiones; se inicia automáticamente con BookDB siempre que el ajuste esté activado.

### Ajustes

- **Puerto** — el puerto de red en el que escucha el complemento (predeterminado **7443**). Cámbielo solo si otro programa ya usa ese puerto. Si lo cambia, no hace falta volver a emparejar, pero el dispositivo puede tardar un momento en redescubrir BookDB en el nuevo puerto.
- **Tamaño máximo de imagen** y **calidad JPEG** — cómo se escalan y comprimen las fotos de portada y de página antes de almacenarse. Valores más bajos ahorran espacio; valores más altos conservan más detalle. Esto se aplica a las fotos tomadas en el dispositivo.

## Emparejar un dispositivo

Un dispositivo debe emparejarse una vez antes de poder enviar libros. El emparejamiento intercambia un conjunto de certificados de seguridad para que solo los dispositivos que **usted** ha aprobado puedan conectarse, y todo lo que pasa entre ellos está cifrado.

1. Asegúrese de que el complemento esté activado y en marcha, y de que el dispositivo esté en la **misma red Wi-Fi** que este equipo.
2. Abra **Herramientas › Mantenimiento › Dispositivos** y pulse **Mostrar código de emparejamiento…**. (También hay un botón para gestionar dispositivos en la pestaña Configuración ▸ Complemento que abre el mismo sitio.)
3. Una ventana muestra un **código QR**. En la aplicación BookDB del dispositivo, elija emparejar y escanee el código.
4. Cuando el dispositivo se conecta, la ventana confirma el emparejamiento. Puede emparejar otro dispositivo o cerrar la ventana.

El código de emparejamiento es **de un solo uso y de corta duración**: se renueva por sí solo cada par de minutos y solo puede canjearse una vez. Si un código caduca antes de que lo escanee, aparece otro automáticamente. Si la dirección de red elegida automáticamente no es la que el dispositivo puede alcanzar, elija otra de la lista desplegable antes de escanear.

Puede emparejar hasta **cinco dispositivos**. Cada uno aparece en la lista de dispositivos con el nombre que se le dio, cuándo se emparejó y cuándo se usó por última vez. Elimine un dispositivo con **Quitar** para liberar su plaza; un dispositivo eliminado debe emparejarse de nuevo antes de poder reconectarse.

## El cortafuegos

La primera vez que el complemento se inicia, Windows y macOS preguntan si permite que BookDB acepte conexiones entrantes. Debe permitirlo, o los dispositivos no podrán alcanzar BookDB. **En Linux no se le pregunta nada**: si hay un cortafuegos en ejecución, tendrá que abrir los puertos usted mismo.

- **Windows** — permita BookDB en **redes privadas** (su red doméstica o de oficina). **No** necesita permitirlo en redes públicas. Si descartó el aviso o pulsó *Cancelar*, no pasará ninguna conexión; vuelva a permitirlo en **Seguridad de Windows › Firewall y protección de red › Permitir una aplicación a través del firewall**, marque la casilla **Privada** de BookDB, o elimine la regla de bloqueo de BookDB para que el aviso vuelva a aparecer la próxima vez.
- **macOS** — permita las conexiones entrantes para BookDB cuando se le pregunte. Puede revisarlo más tarde en **Ajustes del sistema › Red › Firewall**.
- **Linux** — no aparecerá ninguna pregunta. Si `ufw` o `firewalld` está en ejecución, bloquea los puertos entrantes de forma predeterminada, y el puerto del complemento (**7443** de forma predeterminada) debe estar abierto para **TCP y UDP**: TCP transporta la conexión y UDP es como un dispositivo vuelve a encontrar este equipo cuando cambia su dirección. Abrir solo TCP deja el emparejamiento y la navegación funcionando mientras el redescubrimiento falla en silencio, algo que suele advertirse mucho después como un fallo intermitente tras un cambio de dirección. Para `ufw`: `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` + `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto udp`, sustituyendo `192.168.1.0/24` por su propia red. Para `firewalld`: `sudo firewall-cmd --permanent --add-port=7443/tcp --add-port=7443/udp` + `sudo firewall-cmd --reload`. Si cambió el puerto del complemento, use su propio puerto en ambos comandos.

El complemento solo escucha en su red local, y solo mientras está activado.

## Si un dispositivo no puede conectarse

- **¿Ambos en la misma red?** El dispositivo y este equipo deben estar en la misma Wi-Fi. Una red de «invitados» suele estar aislada de la principal y no funcionará.
- **Cortafuegos** — revise las notas sobre el cortafuegos de arriba; un puerto bloqueado es la causa más habitual.
- **¿El complemento en marcha?** La línea de Estado en la pestaña Configuración ▸ Complemento debe mostrarlo como en marcha. Si no pudo iniciarse, cambie el puerto y vuelva a guardar.
- **¿Sigue emparejado?** Si el dispositivo se quitó de la lista de dispositivos, o si ha pasado mucho tiempo, empárejelo de nuevo con un código nuevo.
- **Suspensión** — si este equipo se suspendió, el complemento se reanuda al despertar; el dispositivo se reconecta por sí solo en cuanto ambos estén despiertos y en la red.

## Qué puede y qué no puede hacer el dispositivo

Un dispositivo emparejado puede escanear ISBN, tomar fotos de portada y de página, enviarlas para su catalogación y explorar la biblioteca para comprobar si ya posee un libro. Trabaja sobre la biblioteca que esté abierta en ese momento en BookDB —incluida una biblioteca almacenada en un servidor de base de datos remoto—. No puede cambiar la configuración de BookDB ni eliminar otros dispositivos; eso permanece en este equipo.
