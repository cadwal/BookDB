# Guía de importación

BookDB puede importar su colección de libros existente desde una copia de seguridad de Readerware — ya sea el archivo zip de copia de seguridad o la carpeta de copia de seguridad extraída.

## Flujo del asistente de importación

1. **Selección de archivo** — Elegir un archivo .zip de copia de seguridad o carpeta extraída
2. **Vista previa** — Recuento de registros, cobertura de campos, duplicados
3. **Opciones** — Establecer la colección de destino y opciones de importación
4. **Progreso de importación** — Ver el progreso mientras se importan los registros
5. **Informe final** — Revisar el informe de resultados

## Instrucciones paso a paso

## Paso 1 — Seleccionar un archivo

Abra el asistente de importación desde **Archivo > Importar copia de seguridad de Readerware…** o la barra de herramientas.

Haga clic en **Examinar** y seleccione uno de los siguientes:
- Un **zip de copia de seguridad** de Readerware (.zip) — un archivo de copia de seguridad creado con la función *Copia de seguridad* de Readerware
- Una **carpeta de copia de seguridad** de Readerware — el contenido extraído de dicho zip

Haga clic en **Siguiente** para continuar con la vista previa.

## Paso 2 — Vista previa

Antes de escribir ningún dato, BookDB analiza la copia de seguridad y muestra:
- **Recuento de registros** — cuántos libros se encontraron
- **Cobertura de campos** — qué campos se detectaron y cuántos registros tienen cada campo completado
- **ISBN duplicados** — ISBN que ya existen en su colección
- **Problemas de codificación** — problemas de codificación de caracteres encontrados en el archivo

Revise la vista previa detenidamente. No se importa ningún dato hasta que confirme en el Paso 4.

Haga clic en **Siguiente** para continuar con las opciones de importación.

## Paso 3 — Opciones de importación

**Colección de destino** — elija a qué colección (Ficción, No ficción, Cómics, etc.) se asignarán los libros importados. Puede cambiar esto más adelante editando libros individuales.

**Gestión de duplicados** — si ya existe un libro con el mismo ISBN en su colección, BookDB puede:
- Omitir el duplicado (predeterminado)
- Sobrescribir el registro existente
- Preguntarle cada vez

Haga clic en **Siguiente** para iniciar la importación.

## Paso 4 — Progreso de la importación

BookDB importa registros por lotes. La barra de progreso muestra:
- Cuántos registros se han procesado
- Los registros que se omitieron o fallaron

Puede cancelar la importación en cualquier momento. Los registros parcialmente importados se conservan.

## Paso 5 — Informe de importación

El informe final muestra:
- **Registros importados** — guardados correctamente en la base de datos
- **Registros omitidos** — duplicados o registros con errores
- **Campos faltantes** — campos que estaban vacíos en el archivo de importación
- **Problemas de codificación** — problemas de caracteres encontrados

Haga clic en **Finalizar** para cerrar el asistente. Su lista de libros se actualiza automáticamente.

## Formatos de archivo compatibles

| Formato | Creado por | Notas |
|---------|-----------|-------|
| Zip | Readerware > Copia de seguridad | Archivo de copia de seguridad que contiene datos de libros e imágenes de portada |
| Carpeta | Extraer el zip | El contenido extraído de un zip de copia de seguridad de Readerware |

## Imágenes de portada

Las imágenes de portada incrustadas en el archivo de copia de seguridad se importan automáticamente y se asocian a cada libro.

## Varias imágenes del mismo tipo

Un libro puede acabar con más de una imagen del mismo tipo — Readerware suele almacenar varias imágenes de portada o miniatura por libro, y es posible que todas se importen como el mismo tipo (por ejemplo, dos imágenes de *Portada*). BookDB conserva todas las imágenes, pero cada tipo muestra solo una en la vista previa: la que tiene el orden más bajo.

Estos libros se señalan en la lista de libros con una insignia **!** en la miniatura ("Tipos de imagen duplicados — comprueba la pestaña Imágenes").

Para resolverlo, abra el libro para editarlo y vaya a la pestaña **Imágenes**. Cuando un tipo tiene dos o más imágenes, aparece la sección **Administrar todas las imágenes**, que enumera cada imagen. Para cada una puede:

- **Reasignarla a un tipo de imagen diferente** — por ejemplo, cambiar una segunda *Portada* a *Contraportada* o *Lomo*.
- **Moverla hacia arriba o hacia abajo dentro del tipo** — la imagen superior (con el orden más bajo) se convierte en la vista previa de ese tipo.
- **Eliminar la imagen**.

Guarde el libro para conservar los cambios. Cuando cada tipo tenga como máximo una imagen, la insignia **!** desaparece.

## Importar desde una base de datos de Readerware activa

Si no tiene una copia de seguridad pero todavía tiene su base de datos de Readerware activa (la carpeta `.rw4`, p. ej. `MyBooks.rw4`), BookDB puede leerla directamente:

1. Abra **Herramientas > Importar base de datos de Readerware…**.
2. Haga clic en **Examinar** y seleccione su carpeta de base de datos `.rw4`.
3. Haga clic en **Convertir**. BookDB copia primero la base de datos — su original nunca se abre ni se modifica — y la convierte en una carpeta de copia de seguridad.
4. Cuando finalice la conversión, haga clic en **Abrir el asistente de importación** para continuar con los mismos pasos de vista previa, configuración e importación descritos arriba.

Esto requiere una configuración única: indique la carpeta de herramientas HSQLDB + Java en **Configuración > Importar**. Esa carpeta debe contener `jre\bin\java.exe` y `lib\hsqldb.jar`.

### Versión de Readerware compatible

Esta función admite bases de datos de **Readerware 4** — el formato `DBCATALOG40`, almacenado como una base de datos HSQLDB 1.8.x. Se importan las imágenes de portada y miniatura en formato **JPEG, PNG, GIF o BMP**.

## Solución de problemas

**«No se encontraron registros»** — El archivo puede estar vacío o no ser una copia de seguridad válida de Readerware. Verifique que se creó con la función Copia de seguridad de Readerware, no con una exportación.

**«Se detectaron problemas de codificación»** — BookDB gestiona la codificación de caracteres automáticamente. Si ve caracteres ilegibles en la vista previa, la copia de seguridad puede estar dañada — intente crear una nueva copia de seguridad desde Readerware.

**Se muestran muchos duplicados** — Si ya ha importado algunos libros mediante búsqueda por ISBN, aparecerán como duplicados. Elija «Omitir» para evitar sobrescribir sus registros revisados manualmente.
