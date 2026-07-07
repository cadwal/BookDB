# Bancos de dados remotos

Por padrão, o BookDB guarda sua biblioteca em um arquivo SQLite local — nenhuma configuração é necessária. Se você quiser acessar a mesma biblioteca de vários computadores, pode mantê-la em um servidor de banco de dados: **PostgreSQL** ou **MySQL / MariaDB**. Todos os recursos do BookDB funcionam da mesma forma, onde quer que a biblioteca esteja armazenada.

## Escolher o mecanismo de banco de dados

Abra **Ferramentas › Configurações › Banco de dados**. Em **Mecanismo de banco de dados**, você escolhe entre:

- **Arquivo local (SQLite)** — a biblioteca padrão para um único computador
- **Servidor PostgreSQL**
- **Servidor MySQL / MariaDB**

As opções de servidor exigem um chaveiro do sistema operacional (um cofre seguro de credenciais). O BookDB guarda a senha do servidor **somente** no chaveiro — ela nunca é gravada em um arquivo de configuração e não há alternativa em texto simples. Se não houver chaveiro disponível no sistema, as opções de servidor ficam desativadas.

Para uma conexão com servidor, você informa:

- **Host** e **porta** — a porta padrão é 5432 para PostgreSQL e 3306 para MySQL/MariaDB
- Nome do **banco de dados**
- **Nome de usuário** e **senha**
- **Modo TLS/SSL** — as opções disponíveis dependem do mecanismo escolhido

Se já houver uma senha salva para a conexão, uma dica avisa e o campo de senha pode ficar em branco.

**Testar conexão** verifica as configurações antes de salvar. Em caso de sucesso, mostra a versão do servidor e quantos livros o banco de dados contém. Em caso de falha, informa o que deu errado: credenciais incorretas, conexão recusada, tempo esgotado, problema de TLS ou versão de servidor sem suporte.

Ao salvar uma troca de mecanismo, você é convidado a reiniciar — **o novo mecanismo só entra em vigor depois que o BookDB reinicia**. Se o servidor estiver inacessível na próxima vez que o BookDB iniciar, uma caixa de diálogo oferece **Tentar novamente**, **Abrir configurações** ou **Sair**.

## Requisitos de versão do servidor

- **PostgreSQL 12 ou mais recente** — necessário para a busca em texto completo
- **MySQL 8.0 ou mais recente** / **MariaDB 10.6 ou mais recente**

A versão é verificada ao testar a conexão e novamente na inicialização; um servidor antigo demais é rejeitado com uma mensagem indicando a versão exigida.

## Mover a biblioteca entre mecanismos

**Ferramentas › Manutenção › Mover biblioteca** copia a biblioteca inteira entre dois mecanismos quaisquer — por exemplo, do arquivo SQLite local para um novo servidor PostgreSQL, ou de volta.

A mudança foi projetada para ser segura:

- Um **backup CSV de segurança da origem** é sempre feito antes de qualquer cópia.
- Se o banco de dados de destino já contiver dados, o BookDB também faz backup do destino, e a mudança só começa depois que você marca expressamente **Entendo — substituir todos os dados no banco de dados de destino**.
- Em um destino vazio, o esquema é criado automaticamente.
- Após a cópia, as contagens de linhas de origem e destino são comparadas; a mudança só é considerada concluída quando coincidem.
- Opcionalmente, o BookDB muda o banco de dados ativo para o destino e reinicia.

## Usar a biblioteca em vários computadores

Uma biblioteca em servidor registra os clientes BookDB conectados, atualizados por um sinal de atividade a cada 60 segundos:

- Se outro cliente parecer conectado quando você iniciar, o BookDB avisa. Você pode **Sair** ou escolher **Conectar mesmo assim** — esse botão fica disponível após um atraso de 3 segundos.
- Um cliente que travou sem se desconectar deixa de contar como conectado após cerca de 3 minutos.

Independentemente da sessão no servidor, só uma instância do BookDB pode ser executada por usuário no mesmo computador — iniciar uma segunda traz para a frente a janela já aberta.

Se a conexão com o servidor cair enquanto você trabalha, o BookDB informa e oferece **Continuar aguardando** (recomendado) ou **Sair**.

## Backups de uma biblioteca em servidor

O backup baseado em arquivo se aplica apenas ao arquivo SQLite local. Quando a biblioteca está em um servidor, **Backup...** e os backups automáticos sempre produzem o **arquivo CSV** — a caixa de diálogo informa isso em vez de falhar. Um backup de arquivo SQLite não pode ser restaurado em uma biblioteca em servidor; use um backup CSV ou volte primeiro para o banco de dados local.
