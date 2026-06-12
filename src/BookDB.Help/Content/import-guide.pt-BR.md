# Guia de importação

O BookDB pode importar sua coleção de livros existente de um backup do Readerware — o próprio arquivo zip de backup ou a pasta de backup extraída.

## Fluxo do assistente de importação

1. **Seleção de arquivo** — Escolha um arquivo .zip de backup ou uma pasta de backup extraída
2. **Pré-visualização** — Pré-visualização: contagem de registros, cobertura de campos, duplicatas
3. **Configurações** — Defina a coleção de destino e as opções de importação
4. **Progresso da importação** — Acompanhe o progresso enquanto os registros são importados
5. **Relatório de resumo** — Revise o relatório de resultados

## Instruções passo a passo

## Etapa 1 — Selecionar um arquivo

Abra o assistente de importação em **Arquivo > Importar backup do Readerware…** ou na barra de ferramentas.

Clique em **Procurar** e selecione um dos seguintes:
- Um **arquivo zip de backup** do Readerware (.zip) — um arquivo de backup do Readerware criado com a função *Backup* do Readerware
- Uma **pasta de backup** do Readerware — o conteúdo extraído de tal zip

Clique em **Próximo** para prosseguir para a pré-visualização.

## Etapa 2 — Pré-visualização

Antes de qualquer dado ser gravado, o BookDB analisa o backup e mostra:
- **Contagem de registros** — quantos livros foram encontrados
- **Cobertura de campos** — quais campos foram detectados e quantos registros têm cada campo preenchido
- **ISBNs duplicados** — ISBNs que já existem em sua coleção
- **Problemas de codificação** — quaisquer problemas de codificação de caracteres encontrados no arquivo

Revise a pré-visualização com cuidado. Nenhum dado é importado até você confirmar na Etapa 4.

Clique em **Próximo** para prosseguir para as configurações de importação.

## Etapa 3 — Opções de importação

**Coleção de destino** — escolha para qual coleção (Ficção, Não-Ficção, Quadrinhos, etc.) os livros importados serão atribuídos. Você pode alterar isso depois editando livros individualmente.

**Tratamento de duplicatas** — se um livro com o mesmo ISBN já existir em sua coleção, o BookDB pode:
- Ignorar a duplicata (padrão)
- Substituir o registro existente
- Perguntar cada vez

Clique em **Próximo** para iniciar a importação.

## Etapa 4 — Progresso da importação

O BookDB importa registros em lotes. A barra de progresso mostra:
- Quantos registros foram processados
- Quaisquer registros ignorados ou com falha

Você pode cancelar a importação a qualquer momento. Os registros parcialmente importados são retidos.

## Etapa 5 — Relatório de importação

O relatório final mostra:
- **Registros importados** — salvos com sucesso no banco de dados
- **Registros ignorados** — duplicatas ou registros com erros
- **Campos ausentes** — campos que estavam vazios em todo o arquivo de importação
- **Problemas de codificação** — quaisquer problemas de caracteres encontrados

Clique em **Concluir** para fechar o assistente. Sua lista de livros é atualizada automaticamente.

## Formatos de arquivo suportados

| Formato | Criado por | Observações |
|---------|-----------|-------------|
| Zip | Readerware > Backup | Arquivo de backup contendo dados de livros e imagens de capa |
| Pasta | Extrair o zip | O conteúdo extraído de um zip de backup do Readerware |

## Imagens de capa

As imagens de capa incorporadas no arquivo de backup são importadas automaticamente e associadas a cada livro.

## Várias imagens do mesmo tipo

Um livro pode acabar com mais de uma imagem do mesmo tipo — o Readerware costuma armazenar várias imagens de capa ou miniatura por livro, e elas podem ser importadas todas como o mesmo tipo (por exemplo, duas imagens de *Capa frontal*). O BookDB mantém todas as imagens, mas cada tipo mostra apenas uma na pré-visualização: a de menor ordem.

Esses livros são sinalizados na lista com um selo **!** na miniatura ("Tipos de imagem duplicados — verifique a aba Imagens").

Para resolver, abra o livro para edição e vá até a aba **Imagens**. Sempre que um tipo tiver duas ou mais imagens, aparece a seção **Gerenciar todas as imagens**, listando cada imagem. Para cada uma você pode:

- **Reatribuí-la a um tipo de imagem diferente** — por exemplo, mudar uma segunda *Capa frontal* para *Contracapa* ou *Lombada*.
- **Movê-la para cima ou para baixo dentro do tipo** — a imagem do topo (de menor ordem) se torna a pré-visualização daquele tipo.
- **Remover a imagem**.

Salve o livro para manter as alterações. Quando cada tipo tiver no máximo uma imagem, o selo **!** desaparece.

## Importar de um banco de dados ativo do Readerware

Se você não tem um backup, mas ainda tem seu banco de dados ativo do Readerware (a pasta `.rw4`, por ex. `MyBooks.rw4`), o BookDB pode lê-lo diretamente:

1. Abra **Ferramentas > Importar banco de dados do Readerware…**.
2. Clique em **Procurar** e selecione sua pasta de banco de dados `.rw4`.
3. Clique em **Converter**. O BookDB copia o banco de dados primeiro — seu original nunca é aberto nem modificado — e o converte em uma pasta de backup.
4. Quando a conversão terminar, clique em **Abrir o assistente de importação** para continuar pelas mesmas etapas de visualização, configurações e importação descritas acima.

Isso requer uma configuração única: defina a pasta de ferramentas HSQLDB + Java em **Configurações > Importar**. Essa pasta deve conter `jre\bin\java.exe` e `lib\hsqldb.jar`.

### Versão do Readerware compatível

Este recurso oferece suporte a bancos de dados do **Readerware 4** — o formato `DBCATALOG40`, armazenado como um banco de dados HSQLDB 1.8.x. Imagens de capa e miniatura nos formatos **JPEG, PNG, GIF ou BMP** são importadas.

## Solução de problemas

**"Nenhum registro encontrado"** — O arquivo pode estar vazio ou não ser um backup válido do Readerware. Verifique se foi criado com a função Backup do Readerware, não com uma exportação.

**"Problemas de codificação detectados"** — O BookDB trata a codificação de caracteres automaticamente. Se você vir caracteres ilegíveis na pré-visualização, o arquivo de backup pode estar danificado — tente criar um novo backup no Readerware.

**Muitas duplicatas exibidas** — Se você já importou alguns livros por pesquisa de ISBN, eles aparecerão como duplicatas. Escolha "Ignorar" para evitar sobrescrever seus registros revisados manualmente.
