# Glosario de campos

Descripciones de todos los campos en BookDB. Los campos marcados como *opcionales* no necesitan completarse para guardar un libro.

## Información del título

| Campo | Descripción |
|-------|-------------|
| Título | El título principal del libro. Obligatorio. |
| Subtítulo | Un título secundario, normalmente mostrado bajo el título principal en la portada. *Opcional.* |
| Título alternativo | Un título alternativo o en el idioma original (p. ej., el título en inglés de una obra traducida). *Opcional.* |

## Colaboradores

| Campo | Descripción |
|-------|-------------|
| Autores / Colaboradores | Las personas involucradas en la creación del libro — Autor, Editor, Ilustrador, Diseñador y otros roles. Cada colaborador es un registro de persona vinculado al libro con un rol. |

## Detalles de publicación

| Campo | Descripción |
|-------|-------------|
| Editorial | La editorial que publicó el libro. *Opcional.* |
| Lugar de publicación | La ciudad o país de publicación. *Opcional.* |
| Fecha de publicación | El año de publicación. Se almacena como texto para admitir fechas parciales o aproximadas como «ca. 1950». *Opcional.* |
| Fecha de copyright | El año de copyright, que puede diferir de la fecha de publicación en ediciones posteriores. *Opcional.* |
| Formato | El formato físico: Tapa dura, Rústica, Letra grande, etc. *Opcional.* |
| Edición | La edición del libro: Primera, Segunda, Revisada, etc. *Opcional.* |
| Páginas | El número total de páginas. *Opcional.* |
| Idioma | El idioma del texto del libro. *Opcional.* |

## Identificadores

| Campo | Descripción |
|-------|-------------|
| ISBN | El Número Internacional Normalizado del Libro (ISBN-10 o ISBN-13). Utilizado para buscar metadatos y detectar duplicados. *Opcional.* |
| ISSN | El Número Internacional Normalizado de Publicaciones Seriadas, para publicaciones periódicas. *Opcional.* |
| LCCN | Número de control de la Biblioteca del Congreso. *Opcional.* |
| Clasificación decimal de Dewey | Código de clasificación decimal de Dewey. *Opcional.* |
| Signatura topográfica | La signatura topográfica de la biblioteca para la ubicación en estantería. *Opcional.* |

## Serie

| Campo | Descripción |
|-------|-------------|
| Serie | La serie a la que pertenece el libro, si corresponde. *Opcional.* |
| Número de serie | La posición de este libro dentro de la serie (p. ej., «3» o «3.5»). *Opcional.* |

## Su ejemplar

| Campo | Descripción |
|-------|-------------|
| Ejemplares | El número de ejemplares físicos que posee. El valor predeterminado es 1. |
| Estado | El estado físico de su ejemplar: Muy bueno, Bueno, Aceptable, Regular, Malo, etc. *Opcional.* |
| Ubicación | La estantería, habitación o lugar de almacenamiento donde se guarda este ejemplar. *Opcional.* |
| Propietario | El propietario de este ejemplar (útil para colecciones compartidas). *Opcional.* |
| Firmado | Indica si este es un ejemplar firmado. |
| Agotado | Indica si el libro está marcado como agotado. |

## Seguimiento de lectura

| Campo | Descripción |
|-------|-------------|
| Estado de lectura | Su estado de lectura: Por leer, Leyendo, Leído, Abandonado, etc. *Opcional.* |
| Veces leído | Cuántas veces ha leído este libro. |
| Última lectura | La fecha en que terminó de leer este libro por última vez. *Opcional.* |
| Valoración | Su valoración personal. *Opcional.* |
| Favorito | Indica si este libro está marcado como favorito. |
| Nivel de lectura | El nivel de lectura previsto (edad o curso). *Opcional.* |

## Compra y valor

| Campo | Descripción |
|-------|-------------|
| Precio de compra | El precio que pagó por este ejemplar. *Opcional.* |
| Moneda de compra | La moneda del precio de compra (p. ej., EUR, USD, SEK). *Opcional.* |
| Lugar de compra | Dónde compró el libro. *Opcional.* |
| Fecha de compra | La fecha en que compró el libro. *Opcional.* |
| Precio de lista | El precio de venta recomendado por el editor. *Opcional.* |
| Moneda del precio de lista | La moneda del precio de lista. *Opcional.* |
| Valor del ejemplar | El valor monetario estimado de este ejemplar (p. ej., para seguros). *Opcional.* |
| Fecha de valoración | La fecha en que se estimó el valor. *Opcional.* |

## Descripción y notas

| Campo | Descripción |
|-------|-------------|
| Palabras clave | Etiquetas de texto libre para su propio uso. *Opcional.* |
| Comentarios | Sus notas personales sobre este libro. *Opcional.* |
| Información del libro | Una descripción extendida o sinopsis. *Opcional.* |
| Dimensiones | Las dimensiones físicas del libro (p. ej., «24 × 16 × 3 cm»). *Opcional.* |
| Peso | El peso físico del libro. *Opcional.* |

## Campos del sistema y de origen

| Campo | Descripción |
|-------|-------------|
| Origen | El origen del registro de catálogo (p. ej., Importado, Manual, Búsqueda por ISBN). *Opcional.* |
| Enlace multimedia | Una URL a medios relacionados o la página del editor de este libro. *Opcional.* |
| Categorías | Las categorías de colección a las que pertenece este libro (p. ej., Ficción, Cómics). Se gestiona en el panel de filtros. |
| Añadido | La fecha y hora en que se creó este registro en BookDB. Se establece automáticamente. |
| Actualizado | La fecha y hora de la última modificación. Se actualiza automáticamente al guardar. |
