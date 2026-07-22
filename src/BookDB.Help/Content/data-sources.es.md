# Acerca de las fuentes de datos

Cuando cataloga un libro por ISBN (Ctrl+I o el botón de la barra de herramientas), BookDB obtiene metadatos de cuatro fuentes públicas simultáneamente.

## Flujo de búsqueda

1. Introduce el ISBN
2. BookDB consulta las cuatro fuentes en paralelo — **Google Books**, **Open Library**, **Libris KB**, **IsbnSearch.org**
3. Se abre el diálogo **Revisión de fusión** — usted elige qué campos aceptar de cada fuente
4. Registro de libro guardado

## Google Books

**URL:** https://books.google.com (API: books.googleapis.com)

Google Books es la mayor base de datos de libros de propósito general, con amplia cobertura de títulos en inglés y títulos internacionales populares.

**Campos proporcionados habitualmente:**
- Título, Subtítulo, Autores
- Editorial, Fecha de publicación
- Descripción (Información del libro)
- Número de páginas
- Idioma
- ISBN-10 e ISBN-13
- Imagen de portada (miniatura y grande)
- Categorías

**Notas:**
- Funciona sin clave, pero las solicitudes sin autenticar comparten una pequeña cuota diaria y suelen sufrir límite de peticiones (429). Añade una clave de API personal (más abajo) para usar tu propia cuota
- La cobertura es mayor para publicaciones comerciales posteriores a 1980
- Los nombres de los autores pueden no coincidir siempre con el formato preferido

**Obtener una clave de API de Google Books (opcional)**

Sin una clave, BookDB comparte una pequeña cuota diaria anónima con el resto de llamadas sin autenticar, por lo que Google Books sufre límite de peticiones con frecuencia — un aviso en el diálogo de revisión de combinación indica las fuentes omitidas. Una clave personal gratuita traslada tus búsquedas a tu propia cuota:

1. Inicia sesión en la **Google Cloud Console** en https://console.cloud.google.com.
2. Crea un proyecto nuevo o selecciona uno existente.
3. Abre **APIs & Services → Library**, busca **Books API** y haz clic en **Enable**.
4. Abre **APIs & Services → Credentials**, haz clic en **Create credentials → API key** y copia la clave.
5. Recomendado: edita la clave y, en **API restrictions**, restríngela a la **Books API**.
6. En BookDB, abre **Configuración → Búsqueda**, pega la clave en **Google Books** y haz clic en **Guardar**.

La clave se aplica en la siguiente búsqueda, sin necesidad de reiniciar. Vacía el campo y guarda para volver a la cuota compartida.

## Open Library

**URL:** https://openlibrary.org

Open Library es un catálogo de acceso libre mantenido por Internet Archive. Da prioridad a la exhaustividad sobre la calidad — los registros pueden tener más campos pero un formato menos uniforme.

**Campos proporcionados habitualmente:**
- Título, Autores
- Editorial, Fecha de publicación, Lugar de publicación
- Número de páginas
- ISBN, LCCN, Dewey Decimal
- Imagen de portada

**Notas:**
- Mantenido por la comunidad — la calidad de los datos varía
- Especialmente útil para libros antiguos o agotados
- Suele proporcionar identificadores (LCCN, Dewey) que Google Books no ofrece

## Libris KB

**URL:** https://libris.kb.se

Libris es el catálogo nacional sueco, mantenido por la Biblioteca Nacional de Suecia (Kungliga biblioteket). Tiene una excelente cobertura de publicaciones suecas y traducciones al sueco.

**Campos proporcionados habitualmente:**
- Título, Autores
- Editorial, Año de publicación
- Idioma
- ISBN
- Información de serie
- Dewey Decimal, Signatura

**Notas:**
- La mejor fuente para libros publicados en Suecia o traducidos al sueco
- Las descripciones y resúmenes pueden estar en sueco
- La cobertura de títulos no suecos es limitada

## IsbnSearch.org

**URL:** https://isbnsearch.org

IsbnSearch.org es un servicio gratuito de búsqueda ISBN que proporciona datos bibliográficos básicos extraídos de sus páginas web. Sirve como fuente complementaria útil para ISBN que no devuelven resultados en las fuentes basadas en API.

**Campos proporcionados habitualmente:**
- Título, Autores
- Editorial, Fecha de publicación
- Imagen de portada

**Notas:**
- Los datos se extraen mediante análisis HTML — el formato puede ser menos consistente que en las fuentes de API
- Se utiliza mejor como fuente complementaria junto con Google Books, Open Library y Libris KB
## Revisión de fusión

Después de que BookDB obtiene los resultados de todas las fuentes disponibles, el diálogo **Revisión de fusión** muestra todos los campos recuperados en paralelo:

| Campo | Actual | Google Books | Open Library | Libris KB |
|-------|--------|-------------|--------------|-----------|
| Título | — | The Great... | The Great... | — |
| Autor | — | Fitzgerald, F. | Fitzgerald, F. | — |
| Editorial | — | Scribner | — | — |
| Páginas | — | 180 | 172 | — |

Para cada campo puede:
- **Aceptar** un valor de una fuente (haga clic en el valor para seleccionarlo)
- **Mantener** su valor actual
- **Aceptar todo** para tomar todos los valores entrantes de una vez

Al hacer clic en **Guardar**, solo se actualizan los campos que aceptó. Sus datos existentes nunca se sobrescriben automáticamente.

## Cuando una fuente no devuelve resultados

Si una fuente no devuelve resultados para un ISBN:
- La columna de la fuente simplemente no aparece en la tabla de fusión
- Las demás fuentes no se ven afectadas
- Esto es normal para libros más recientes, publicaciones regionales o ISBN inusuales

## Límites de frecuencia

BookDB respeta automáticamente los límites de frecuencia de cada API. Durante la recatalogación masiva (Herramientas > Recatalogar), las solicitudes se espacian para que nunca sea bloqueado de ninguna fuente.
