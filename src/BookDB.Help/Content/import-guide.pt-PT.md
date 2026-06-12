# Guia de importação

O BookDB pode importar a sua coleção de livros existente a partir de uma cópia de segurança do Readerware — o próprio ficheiro zip ou a pasta extraída.

## Fluxo do assistente de importação

1. **Seleção de ficheiro** — Escolha um ficheiro .zip de backup ou uma pasta de backup extraída
2. **Pré-visualização** — Pré-visualização: contagem de registos, cobertura de campos, duplicados
3. **Definições** — Defina a coleção de destino e as opções de importação
4. **Progresso da importação** — Acompanhe o progresso enquanto os registos são importados
5. **Relatório de resumo** — Reveja o relatório de resultados

## Instruções passo a passo

## Passo 1 — Selecionar um ficheiro

Abra o assistente de importação em **Ficheiro > Importar cópia de segurança do Readerware…** ou na barra de ferramentas.

Clique em **Procurar** e selecione um dos seguintes:
- Um **ficheiro zip de backup** do Readerware (.zip) — um arquivo de backup do Readerware criado com a função *Backup* do Readerware
- Uma **pasta de backup** do Readerware — o conteúdo extraído de tal zip

Clique em **Seguinte** para prosseguir para a pré-visualização.

## Passo 2 — Pré-visualização

Antes de quaisquer dados serem escritos, o BookDB analisa o backup e mostra:
- **Contagem de registos** — quantos livros foram encontrados
- **Cobertura de campos** — quais campos foram detetados e quantos registos têm cada campo preenchido
- **ISBNs duplicados** — ISBNs que já existem na sua coleção
- **Problemas de codificação** — quaisquer problemas de codificação de caracteres encontrados no ficheiro

Reveja a pré-visualização com cuidado. Nenhum dado é importado até confirmar no Passo 4.

Clique em **Seguinte** para prosseguir para as definições de importação.

## Passo 3 — Opções de importação

**Coleção de destino** — escolha para qual coleção (Ficção, Não-ficção, Banda desenhada, etc.) os livros importados serão atribuídos. Pode alterar isto posteriormente editando livros individualmente.

**Tratamento de duplicados** — se um livro com o mesmo ISBN já existir na sua coleção, o BookDB pode:
- Ignorar o duplicado (predefinição)
- Substituir o registo existente
- Perguntar cada vez

Clique em **Seguinte** para iniciar a importação.

## Passo 4 — Progresso da importação

O BookDB importa registos em lotes. A barra de progresso mostra:
- Quantos registos foram processados
- Quaisquer registos ignorados ou falhados

Pode cancelar a importação a qualquer momento. Os registos parcialmente importados são mantidos.

## Passo 5 — Relatório de importação

O relatório final mostra:
- **Registos importados** — guardados com sucesso na base de dados
- **Registos ignorados** — duplicados ou registos com erros
- **Campos em falta** — campos que estavam vazios em todo o ficheiro de importação
- **Problemas de codificação** — quaisquer problemas de caracteres encontrados

Clique em **Concluir** para fechar o assistente. A sua lista de livros é atualizada automaticamente.

## Formatos de ficheiro suportados

| Formato | Criado por | Notas |
|---------|-----------|-------|
| Zip | Readerware > Backup | Arquivo de backup contendo dados de livros e imagens de capa |
| Pasta | Extrair o zip | O conteúdo extraído de um zip de backup do Readerware |

## Imagens de capa

As imagens de capa incorporadas no arquivo de backup são importadas automaticamente e associadas a cada livro.

## Várias imagens do mesmo tipo

Um livro pode acabar com mais do que uma imagem do mesmo tipo — o Readerware costuma armazenar várias imagens de capa ou miniatura por livro, e estas podem ser importadas todas como o mesmo tipo (por exemplo, duas imagens de *Capa frontal*). O BookDB mantém todas as imagens, mas cada tipo mostra apenas uma na pré-visualização: a de ordem mais baixa.

Estes livros são assinalados na lista com um emblema **!** na miniatura ("Tipos de imagem duplicados — consulte o separador Imagens").

Para resolver, abra o livro para edição e vá ao separador **Imagens**. Sempre que um tipo tiver duas ou mais imagens, aparece a secção **Gerir todas as imagens**, que lista cada imagem. Para cada uma pode:

- **Reatribuí-la a um tipo de imagem diferente** — por exemplo, alterar uma segunda *Capa frontal* para *Contracapa* ou *Lombada*.
- **Movê-la para cima ou para baixo dentro do tipo** — a imagem do topo (de ordem mais baixa) torna-se a pré-visualização desse tipo.
- **Remover a imagem**.

Guarde o livro para manter as alterações. Quando cada tipo tiver no máximo uma imagem, o emblema **!** desaparece.

## Importar de uma base de dados ativa do Readerware

Se não tem uma cópia de segurança, mas ainda tem a sua base de dados ativa do Readerware (a pasta `.rw4`, por ex. `MyBooks.rw4`), o BookDB pode lê-la diretamente:

1. Abra **Ferramentas > Importar base de dados do Readerware…**.
2. Clique em **Procurar** e selecione a sua pasta de base de dados `.rw4`.
3. Clique em **Converter**. O BookDB copia primeiro a base de dados — o seu original nunca é aberto nem modificado — e converte-a numa pasta de cópia de segurança.
4. Quando a conversão terminar, clique em **Abrir o assistente de importação** para continuar pelos mesmos passos de pré-visualização, definições e importação descritos acima.

Isto requer uma configuração única: defina a pasta de ferramentas HSQLDB + Java em **Definições > Importação**. Essa pasta deve conter `jre\bin\java.exe` e `lib\hsqldb.jar`.

### Versão do Readerware suportada

Esta funcionalidade suporta bases de dados do **Readerware 4** — o formato `DBCATALOG40`, armazenado como uma base de dados HSQLDB 1.8.x. As imagens de capa e miniatura nos formatos **JPEG, PNG, GIF ou BMP** são importadas.

## Resolução de problemas

**"Nenhum registo encontrado"** — O ficheiro pode estar vazio ou não ser um backup válido do Readerware. Verifique se foi criado com a função Backup do Readerware, não com uma exportação.

**"Problemas de codificação detetados"** — O BookDB trata a codificação de caracteres automaticamente. Se vir caracteres ilegíveis na pré-visualização, o ficheiro de backup pode estar danificado — tente criar um novo backup no Readerware.

**Muitos duplicados apresentados** — Se já importou alguns livros por pesquisa de ISBN, aparecerão como duplicados. Escolha "Ignorar" para evitar substituir os seus registos revistos manualmente.
