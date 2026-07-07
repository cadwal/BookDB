# Bases de datos remotas

De forma predeterminada, BookDB guarda su biblioteca en un archivo SQLite local, sin necesidad de configuración. Si desea acceder a la misma biblioteca desde varios equipos, puede alojarla en un servidor de base de datos: **PostgreSQL** o **MySQL / MariaDB**. Todas las funciones de BookDB se comportan igual con independencia de dónde esté almacenada la biblioteca.

## Elegir el motor de base de datos

Abra **Herramientas › Configuración › Base de datos**. En **Motor de base de datos** puede elegir entre:

- **Archivo local (SQLite)** — la biblioteca predeterminada para un solo equipo
- **Servidor PostgreSQL**
- **Servidor MySQL / MariaDB**

Las opciones de servidor requieren un llavero del sistema operativo (un almacén seguro de credenciales). BookDB guarda la contraseña del servidor **únicamente** en el llavero: nunca se escribe en un archivo de configuración y no existe alternativa en texto plano. Si su sistema no dispone de llavero, las opciones de servidor aparecen desactivadas.

Para una conexión de servidor debe indicar:

- **Host** y **puerto** — el puerto predeterminado es 5432 para PostgreSQL y 3306 para MySQL/MariaDB
- Nombre de la **base de datos**
- **Nombre de usuario** y **contraseña**
- **Modo TLS/SSL** — las opciones disponibles dependen del motor elegido

Si ya hay una contraseña guardada para la conexión, un aviso lo indica y el campo de contraseña puede quedar vacío.

**Probar conexión** comprueba la configuración antes de guardar. Si la conexión funciona, muestra la versión del servidor y cuántos libros contiene la base de datos. Si falla, indica el motivo: credenciales incorrectas, conexión rechazada, tiempo de espera agotado, problema de TLS o una versión de servidor no compatible.

Al guardar un cambio de motor se le pedirá reiniciar: **el nuevo motor solo entra en vigor tras reiniciar BookDB**. Si el servidor no está accesible la próxima vez que BookDB arranque, un diálogo ofrece **Reintentar**, **Abrir configuración** o **Salir**.

## Requisitos de versión del servidor

- **PostgreSQL 12 o posterior** — necesario para la búsqueda de texto completo
- **MySQL 8.0 o posterior** / **MariaDB 10.6 o posterior**

La versión se comprueba al probar la conexión y de nuevo al arrancar; un servidor demasiado antiguo se rechaza con un mensaje que indica la versión requerida.

## Mover la biblioteca entre motores

**Herramientas › Mantenimiento › Mover biblioteca** copia la biblioteca completa entre dos motores cualesquiera, por ejemplo del archivo SQLite local a un nuevo servidor PostgreSQL, o a la inversa.

El traslado está diseñado para ser seguro:

- Antes de copiar nada se realiza siempre una **copia de seguridad CSV del origen**.
- Si la base de datos de destino ya contiene datos, BookDB también hace copia del destino, y el traslado solo comienza cuando marca expresamente **Entiendo — reemplazar todos los datos en la base de datos de destino**.
- En un destino vacío el esquema se crea automáticamente.
- Tras la copia se comparan los recuentos de filas de origen y destino; el traslado solo se da por completado cuando coinciden.
- Opcionalmente, BookDB cambia la base de datos activa al destino y se reinicia.

## Usar la biblioteca desde varios equipos

Una biblioteca en servidor lleva un registro de los clientes BookDB conectados, actualizado mediante una señal de actividad cada 60 segundos:

- Si al iniciar parece haber otro cliente conectado, BookDB se lo advierte. Puede **Salir** o elegir **Conectar de todos modos**; ese botón se habilita tras una espera de 3 segundos.
- Un cliente que se cerró de forma abrupta sin desconectarse deja de contar como conectado al cabo de unos 3 minutos.

Al margen de la sesión del servidor, solo puede ejecutarse una instancia de BookDB por usuario en el mismo equipo: al iniciar una segunda, se trae al frente la ventana ya abierta.

Si la conexión con el servidor se pierde mientras trabaja, BookDB se lo comunica y ofrece **Seguir esperando** (recomendado) o **Salir**.

## Copias de seguridad de una biblioteca en servidor

La copia de seguridad basada en archivo solo se aplica al archivo SQLite local. Cuando la biblioteca está en un servidor, **Copia de seguridad...** y las copias automáticas generan siempre el **archivo CSV**; el diálogo de copia de seguridad lo indica en lugar de fallar. Una copia de archivo SQLite no puede restaurarse en una biblioteca en servidor: use una copia de archivo CSV o cambie primero a la base de datos local.
