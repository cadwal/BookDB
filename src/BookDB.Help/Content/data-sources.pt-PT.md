# Sobre as fontes de dados

Quando cataloga um livro por ISBN (Ctrl+I ou o botão da barra de ferramentas), o BookDB obtém metadados de quatro APIs públicas simultaneamente.

## Fluxo de pesquisa

1. Introduz um ISBN
2. O BookDB obtém de todas as quatro fontes em paralelo — **Google Books**, **Open Library**, **Libris KB**, **IsbnSearch.org**
3. A caixa de diálogo **Revisão de fusão** abre — escolhe quais campos aceitar de cada fonte
4. O registo do livro é guardado

## Google Books

**URL:** https://books.google.com (API: books.googleapis.com)

O Google Books é a maior base de dados de livros de uso geral, com ampla cobertura de títulos em inglês e títulos internacionais populares.

**Campos tipicamente fornecidos:**
- Título, Subtítulo, Autores
- Editora, Data de publicação
- Descrição (Informações do livro)
- Número de páginas
- Língua
- ISBN-10 e ISBN-13
- Imagem de capa (miniatura e grande)
- Categorias

**Notas:**
- Não é necessária chave de API para pesquisas básicas
- A cobertura é mais forte para publicações comerciais após 1980
- Os nomes dos autores podem nem sempre corresponder ao formato preferido

## Open Library

**URL:** https://openlibrary.org

O Open Library é um catálogo de acesso aberto mantido pelo Internet Archive. Privilegia a completude em detrimento da precisão — os registos podem ter mais campos, mas com formatação menos consistente.

**Campos tipicamente fornecidos:**
- Título, Autores
- Editora, Data de publicação, Local de publicação
- Número de páginas
- ISBNs, LCCN, Classificação decimal de Dewey
- Imagem de capa

**Notas:**
- Mantido pela comunidade — a qualidade dos dados varia
- Particularmente útil para livros mais antigos ou esgotados
- Frequentemente fornece identificadores (LCCN, Dewey) que o Google Books não fornece

## Libris KB

**URL:** https://libris.kb.se

O Libris é o catálogo nacional de bibliotecas da Suécia, mantido pela Biblioteca Nacional da Suécia (Kungliga biblioteket). Tem excelente cobertura de publicações suecas e traduções para sueco.

**Campos tipicamente fornecidos:**
- Título, Autores
- Editora, Ano de publicação
- Língua
- ISBN
- Informações de série
- Classificação decimal de Dewey, Cota

**Notas:**
- Melhor fonte para livros publicados na Suécia ou traduzidos para sueco
- Descrições e resumos podem estar em sueco
- A cobertura de títulos não suecos é limitada

## IsbnSearch.org

**URL:** https://isbnsearch.org

O IsbnSearch.org é um serviço gratuito de pesquisa de ISBN que fornece dados bibliográficos básicos extraídos das suas páginas web. Serve como fonte suplementar útil para ISBNs que não devolvem resultados das fontes baseadas em API.

**Campos tipicamente fornecidos:**
- Título, Autores
- Editora, Data de publicação
- Imagem de capa

**Notas:**
- Os dados são extraídos por análise de HTML — a formatação pode ser menos consistente do que as fontes baseadas em API
- É melhor utilizada como fonte suplementar juntamente com Google Books, Open Library e Libris KB

## Revisão de fusão

Após o BookDB obter resultados de todas as fontes disponíveis, a caixa de diálogo **Revisão de fusão** mostra todos os campos obtidos lado a lado:

| Campo | Atual | Google Books | Open Library | Libris KB |
|-------|-------|-------------|--------------|-----------|
| Título | — | The Great... | The Great... | — |
| Autor | — | Fitzgerald, F. | Fitzgerald, F. | — |
| Editora | — | Scribner | — | — |
| Páginas | — | 180 | 172 | — |

Para cada campo pode:
- **Aceitar** um valor de uma fonte (clique no valor para o selecionar)
- **Manter** o valor atual
- **Aceitar tudo** para aceitar todos os valores recebidos de uma vez

Quando clica em **Guardar**, apenas os campos que aceitou são atualizados. Os dados existentes nunca são substituídos automaticamente.

## Quando uma fonte não devolve resultados

Se uma fonte não devolver resultados para um ISBN:
- A coluna da fonte está simplesmente ausente da tabela de Revisão de fusão
- As outras fontes não são afetadas
- Isto é normal para livros mais recentes, publicações regionais ou ISBNs invulgares

## Limites de velocidade

O BookDB respeita automaticamente os limites de velocidade de cada API. Durante a recatalogação em lote (Ferramentas > Recatalogar), os pedidos são espaçados para que nunca seja bloqueado de nenhuma fonte.
