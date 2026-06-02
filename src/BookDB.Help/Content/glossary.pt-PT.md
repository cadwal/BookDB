# Glossário de campos

Descrições de todos os campos no BookDB. Os campos marcados como *opcionais* não precisam de ser preenchidos para guardar um livro.

## Informações do título

| Campo | Descrição |
|-------|-----------|
| Título | O título principal do livro. Obrigatório. |
| Subtítulo | Uma linha de título secundária, normalmente apresentada abaixo do título principal na capa. *Opcional.* |
| Título alternativo | Um título alternativo ou no idioma original (ex.: o título em inglês de uma obra traduzida). *Opcional.* |

## Colaboradores

| Campo | Descrição |
|-------|-----------|
| Autores / Colaboradores | As pessoas envolvidas na criação do livro — Autor, Editor, Ilustrador, Designer e outras funções. Cada colaborador é um registo de pessoa ligado ao livro com uma função. |

## Detalhes de publicação

| Campo | Descrição |
|-------|-----------|
| Editora | A editora que publicou o livro. *Opcional.* |
| Local de publicação | A cidade ou país de publicação. *Opcional.* |
| Ano de publicação | O ano de publicação. Armazenado como texto para suportar datas parciais ou aproximadas como "ca. 1950". *Opcional.* |
| Data de copyright | O ano de copyright, que pode diferir da data de publicação em edições posteriores. *Opcional.* |
| Formato | O formato físico: Capa dura, Brochura, Letra grande, etc. *Opcional.* |
| Edição | A edição do livro: Primeira, Segunda, Revista, etc. *Opcional.* |
| Páginas | O número total de páginas. *Opcional.* |
| Língua | A língua do texto do livro. *Opcional.* |

## Identificadores

| Campo | Descrição |
|-------|-----------|
| ISBN | O Número Internacional Normalizado do Livro (ISBN-10 ou ISBN-13). Utilizado para pesquisa de metadados e deteção de duplicados. *Opcional.* |
| ISSN | O Número Internacional Normalizado para Publicações em Série, para periódicos. *Opcional.* |
| LCCN | Número de controlo da Biblioteca do Congresso. *Opcional.* |
| Classificação decimal de Dewey | Código de classificação decimal de Dewey. *Opcional.* |
| Cota | Uma cota de biblioteca para localização na estante. *Opcional.* |

## Série

| Campo | Descrição |
|-------|-----------|
| Série | A série à qual o livro pertence, se aplicável. *Opcional.* |
| Número da série | A posição deste livro dentro da série (ex.: "3" ou "3.5"). *Opcional.* |

## O seu exemplar

| Campo | Descrição |
|-------|-----------|
| Cópias | O número de cópias físicas que possui. O valor predefinido é 1. |
| Condição | A condição física do seu exemplar: Excelente, Muito bom, Bom, Razoável, Mau, etc. *Opcional.* |
| Localização | A prateleira, sala ou local de armazenamento onde este exemplar está guardado. *Opcional.* |
| Proprietário | Quem possui este exemplar (útil para coleções partilhadas). *Opcional.* |
| Assinado | Se este é um exemplar assinado. |
| Esgotado | Se o livro está marcado como esgotado. |

## Acompanhamento de leitura

| Campo | Descrição |
|-------|-----------|
| Estado | O seu estado de leitura: Para ler, A ler, Lido, Abandonado, etc. *Opcional.* |
| Número de leituras | Quantas vezes leu este livro. |
| Última leitura | A data em que terminou de ler este livro pela última vez. *Opcional.* |
| Avaliação | A sua avaliação pessoal. *Opcional.* |
| Favorito | Se este livro está marcado como favorito. |
| Nível de leitura | O nível de leitura pretendido (idade ou ano escolar). *Opcional.* |

## Compra e valor

| Campo | Descrição |
|-------|-----------|
| Preço de compra | O preço que pagou por este exemplar. *Opcional.* |
| Moeda de compra | A moeda do preço de compra (ex.: EUR, USD, SEK). *Opcional.* |
| Local de compra | Onde comprou o livro. *Opcional.* |
| Data de compra | A data em que comprou o livro. *Opcional.* |
| Preço de tabela | O preço de venda recomendado pela editora. *Opcional.* |
| Moeda do preço de tabela | A moeda do preço de tabela. *Opcional.* |
| Valor do exemplar | O valor monetário estimado deste exemplar (ex.: para efeitos de seguro). *Opcional.* |
| Data de avaliação | A data em que o valor foi estimado. *Opcional.* |

## Descrição e notas

| Campo | Descrição |
|-------|-----------|
| Palavras-chave | Etiquetas de texto livre para uso próprio. *Opcional.* |
| Comentários | As suas anotações pessoais sobre este livro. *Opcional.* |
| Informações do livro | Uma descrição alargada ou sinopse. *Opcional.* |
| Dimensões | Dimensões físicas do livro (ex.: "24 × 16 × 3 cm"). *Opcional.* |
| Peso | O peso físico do livro. *Opcional.* |

## Campos de sistema e origem

| Campo | Descrição |
|-------|-----------|
| Fonte | A origem do registo de catálogo (ex.: Importado, Manual, Pesquisa por ISBN). *Opcional.* |
| Ligação multimédia | Um URL para multimédia relacionado ou a página da editora para este livro. *Opcional.* |
| Categorias | As categorias da coleção às quais este livro pertence (ex.: Ficção, Banda desenhada). Gerido no painel de filtros. |
| Adicionado | A data e hora em que este registo foi criado no BookDB. Definido automaticamente. |
| Atualizado | A data e hora da última modificação. Atualizado automaticamente ao guardar. |
