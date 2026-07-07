# Bases de dados remotas

Por predefinição, o BookDB guarda a sua biblioteca num ficheiro SQLite local — sem qualquer configuração. Se quiser aceder à mesma biblioteca a partir de vários computadores, pode mantê-la num servidor de base de dados: **PostgreSQL** ou **MySQL / MariaDB**. Todas as funcionalidades do BookDB funcionam da mesma forma, independentemente de onde a biblioteca está guardada.

## Escolher o motor de base de dados

Abra **Ferramentas › Definições › Base de dados**. Em **Motor de base de dados**, pode escolher entre:

- **Ficheiro local (SQLite)** — a biblioteca predefinida para um único computador
- **Servidor PostgreSQL**
- **Servidor MySQL / MariaDB**

As opções de servidor exigem um porta-chaves do sistema operativo (um cofre seguro de credenciais). O BookDB guarda a palavra-passe do servidor **apenas** no porta-chaves — nunca é escrita num ficheiro de configuração e não existe alternativa em texto simples. Se não houver porta-chaves disponível no sistema, as opções de servidor ficam desativadas.

Para uma ligação a um servidor, preencha:

- **Anfitrião** e **porta** — a porta predefinida é 5432 para PostgreSQL e 3306 para MySQL/MariaDB
- Nome da **base de dados**
- **Nome de utilizador** e **palavra-passe**
- **Modo TLS/SSL** — as opções disponíveis dependem do motor escolhido

Se já existir uma palavra-passe guardada para a ligação, uma indicação avisa e o campo da palavra-passe pode ficar vazio.

**Testar ligação** verifica as definições antes de guardar. Em caso de êxito, mostra a versão do servidor e quantos livros a base de dados contém. Em caso de falha, indica o que correu mal: credenciais erradas, ligação recusada, tempo esgotado, problema de TLS ou uma versão de servidor não suportada.

Ao guardar uma mudança de motor, é-lhe pedido que reinicie — **o novo motor só entra em vigor depois de o BookDB reiniciar**. Se o servidor estiver inacessível no próximo arranque do BookDB, uma caixa de diálogo oferece **Tentar novamente**, **Abrir definições** ou **Sair**.

## Requisitos de versão do servidor

- **PostgreSQL 12 ou posterior** — necessário para a pesquisa de texto integral
- **MySQL 8.0 ou posterior** / **MariaDB 10.6 ou posterior**

A versão é verificada ao testar a ligação e novamente no arranque; um servidor demasiado antigo é rejeitado com uma mensagem que indica a versão exigida.

## Mover a biblioteca entre motores

**Ferramentas › Manutenção › Mover biblioteca** copia a biblioteca completa entre dois motores quaisquer — por exemplo, do ficheiro SQLite local para um novo servidor PostgreSQL, ou de volta.

A mudança foi concebida para ser segura:

- É sempre feita uma **cópia de segurança CSV da origem** antes de copiar o que quer que seja.
- Se a base de dados de destino já contiver dados, o BookDB também salvaguarda o destino, e a mudança só começa depois de assinalar expressamente **Compreendo — substituir todos os dados na base de dados de destino**.
- Num destino vazio, o esquema é criado automaticamente.
- Após a cópia, as contagens de linhas de origem e destino são comparadas; a mudança só é dada como concluída quando coincidem.
- Opcionalmente, o BookDB muda a base de dados ativa para o destino e reinicia.

## Utilizar a biblioteca a partir de vários computadores

Uma biblioteca em servidor regista os clientes BookDB ligados, atualizados por um sinal de atividade a cada 60 segundos:

- Se outro cliente parecer ligado quando iniciar, o BookDB avisa-o. Pode **Sair** ou escolher **Ligar mesmo assim** — esse botão fica disponível após um atraso de 3 segundos.
- Um cliente que falhou sem se desligar deixa de contar como ligado ao fim de cerca de 3 minutos.

Independentemente da sessão no servidor, só pode estar em execução uma instância do BookDB por utilizador no mesmo computador — iniciar uma segunda traz para a frente a janela já aberta.

Se a ligação ao servidor cair enquanto trabalha, o BookDB informa-o e oferece **Continuar a aguardar** (recomendado) ou **Sair**.

## Cópias de segurança de uma biblioteca em servidor

A cópia de segurança baseada em ficheiro aplica-se apenas ao ficheiro SQLite local. Quando a biblioteca está num servidor, **Cópia de Segurança...** e as cópias automáticas produzem sempre o **arquivo CSV** — a caixa de diálogo informa-o em vez de falhar. Uma cópia de ficheiro SQLite não pode ser restaurada numa biblioteca em servidor; utilize uma cópia de arquivo CSV ou mude primeiro para a base de dados local.
