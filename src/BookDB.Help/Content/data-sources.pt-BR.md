# Sobre as fontes de dados

Quando você cataloga um livro por ISBN (Ctrl+I ou o botão da barra de ferramentas), o BookDB busca metadados de quatro APIs públicas simultaneamente.

## Fluxo de pesquisa

1. Você insere um ISBN
2. O BookDB busca em todas as quatro fontes em paralelo — **Google Books**, **Open Library**, **Libris KB**, **IsbnSearch.org**
3. A caixa de diálogo **Revisão de mesclagem** é aberta — você escolhe quais campos aceitar de cada fonte
4. O registro do livro é salvo

## Google Books

**URL:** https://books.google.com (API: books.googleapis.com)

O Google Books é o maior banco de dados de livros de uso geral, com ampla cobertura de títulos em inglês e títulos internacionais populares.

**Campos tipicamente fornecidos:**
- Título, Subtítulo, Autores
- Editora, Data de publicação
- Descrição (Informações do livro)
- Número de páginas
- Idioma
- ISBN-10 e ISBN-13
- Imagem de capa (miniatura e grande)
- Categorias

**Observações:**
- Nenhuma chave de API necessária para pesquisas básicas
- A cobertura é mais forte para publicações comerciais após 1980
- Os nomes dos autores podem nem sempre corresponder ao seu formato preferido

## Open Library

**URL:** https://openlibrary.org

O Open Library é um catálogo de acesso aberto mantido pelo Internet Archive. Ele enfatiza a completude em detrimento da precisão — os registros podem ter mais campos, mas com formatação menos consistente.

**Campos tipicamente fornecidos:**
- Título, Autores
- Editora, Data de publicação, Local de publicação
- Número de páginas
- ISBNs, LCCN, Classificação decimal de Dewey
- Imagem de capa

**Observações:**
- Mantido pela comunidade — a qualidade dos dados varia
- Particularmente bom para livros mais antigos ou esgotados
- Frequentemente fornece identificadores (LCCN, Dewey) que o Google Books não fornece

## Libris KB

**URL:** https://libris.kb.se

O Libris é o catálogo nacional de bibliotecas da Suécia, mantido pela Biblioteca Nacional da Suécia (Kungliga biblioteket). Tem excelente cobertura de publicações suecas e traduções para o sueco.

**Campos tipicamente fornecidos:**
- Título, Autores
- Editora, Ano de publicação
- Idioma
- ISBN
- Informações de série
- Classificação decimal de Dewey, Número de chamada

**Observações:**
- Melhor fonte para livros publicados na Suécia ou traduzidos para o sueco
- Descrições e resumos podem estar em sueco
- A cobertura de títulos não suecos é limitada

## IsbnSearch.org

**URL:** https://isbnsearch.org

O IsbnSearch.org é um serviço gratuito de pesquisa de ISBN que fornece dados bibliográficos básicos extraídos de suas páginas web. Serve como uma fonte suplementar útil para ISBNs que não retornam resultados das fontes baseadas em API.

**Campos tipicamente fornecidos:**
- Título, Autores
- Editora, Data de publicação
- Imagem de capa

**Observações:**
- Os dados são extraídos por análise de HTML — a formatação pode ser menos consistente do que as fontes baseadas em API
- Melhor usada como fonte suplementar junto com Google Books, Open Library e Libris KB

## Revisão de mesclagem

Após o BookDB buscar resultados de todas as fontes disponíveis, a caixa de diálogo **Revisão de mesclagem** mostra todos os campos recuperados lado a lado:

| Campo | Atual | Google Books | Open Library | Libris KB |
|-------|-------|-------------|--------------|-----------|
| Título | — | The Great... | The Great... | — |
| Autor | — | Fitzgerald, F. | Fitzgerald, F. | — |
| Editora | — | Scribner | — | — |
| Páginas | — | 180 | 172 | — |

Para cada campo você pode:
- **Aceitar** um valor de uma fonte (clique no valor para selecioná-lo)
- **Manter** seu valor atual
- **Aceitar tudo** para pegar todos os valores recebidos de uma vez

Quando você clica em **Salvar**, apenas os campos que você aceitou são atualizados. Seus dados existentes nunca são substituídos automaticamente.

## Quando uma fonte não retorna resultados

Se uma fonte não retornar resultados para um ISBN:
- A coluna da fonte simplesmente está ausente da tabela de Revisão de mesclagem
- Outras fontes não são afetadas
- Isso é normal para livros mais recentes, publicações regionais ou ISBNs incomuns

## Limites de taxa

O BookDB respeita automaticamente os limites de taxa de cada API. Durante a recatalogação em lote (Ferramentas > Recatalogar), as solicitações são espaçadas para que você nunca seja bloqueado de nenhuma fonte.
