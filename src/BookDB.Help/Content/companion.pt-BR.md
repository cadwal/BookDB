# Complementar

O complemento do BookDB permite catalogar livros a partir de um **telefone ou tablet** na mesma rede local. Você digitaliza um código de barras ISBN (e opcionalmente fotografa a capa) no dispositivo, e o livro flui para a biblioteca em execução neste computador. Nada sai da sua rede: o dispositivo conversa diretamente com o BookDB pelo seu Wi-Fi, nunca pela internet nem por nenhum servidor externo.

O complemento fica **desativado por padrão**. Você o ativa, pareia cada dispositivo uma vez e, a partir daí, o dispositivo se reconecta sozinho sempre que ambos estiverem na mesma rede.

## Ativar o complemento

Abra **Ferramentas › Configurações › Complementar** e marque **Ativar complemento**. A configuração entra em vigor quando você pressiona **Salvar** — não no momento em que marca a caixa —, para que você possa ajustar primeiro a porta e as opções de captura e aplicar tudo de uma vez.

Depois de em execução, a linha de **Status** mostra que o complemento está ouvindo. Se ele não conseguir iniciar — normalmente porque a porta já está em uso —, a caixa de diálogo permanece aberta na aba Complementar e informa o motivo, e a configuração permanece ativada para que você possa mudar a porta e tentar de novo.

Você pode deixar o complemento ativado entre as sessões; ele inicia automaticamente com o BookDB sempre que a configuração estiver ativada.

### Configurações

- **Porta** — a porta de rede em que o complemento escuta (padrão **7443**). Mude-a apenas se outro programa já usar essa porta. Se você a mudar, não é preciso parear de novo, mas o dispositivo pode levar um instante para redescobrir o BookDB na nova porta.
- **Tamanho máximo da imagem** e **qualidade JPEG** — como as fotos de capa e de página são dimensionadas e comprimidas antes de serem armazenadas. Valores mais baixos economizam espaço; valores mais altos mantêm mais detalhes. Isso se aplica às fotos tiradas no dispositivo.

## Parear um dispositivo

Um dispositivo precisa ser pareado uma vez antes de poder enviar livros. O pareamento troca um conjunto de certificados de segurança para que apenas dispositivos que **você** aprovou possam se conectar, e tudo entre eles é criptografado.

1. Verifique se o complemento está ativado e em execução, e se o dispositivo está na **mesma rede Wi-Fi** que este computador.
2. Abra **Ferramentas › Manutenção › Dispositivos** e pressione **Mostrar código de pareamento…**. (Há também um botão para gerenciar dispositivos na aba Configurações ▸ Complementar que abre o mesmo lugar.)
3. Uma janela mostra um **código QR**. No aplicativo BookDB no dispositivo, escolha parear e digitalize o código.
4. Quando o dispositivo se conecta, a janela confirma o pareamento. Você pode parear outro dispositivo ou fechar a janela.

O código de pareamento é **de uso único e de curta duração** — ele se renova sozinho a cada dois minutos, aproximadamente, e só pode ser usado uma vez. Se um código expirar antes de você digitalizá-lo, outro aparece automaticamente. Se o endereço de rede escolhido automaticamente não for o que o dispositivo consegue alcançar, escolha outro na lista suspensa antes de digitalizar.

Você pode parear até **cinco dispositivos**. Cada um aparece na lista de dispositivos com o nome que recebeu, quando foi pareado e quando foi usado pela última vez. Remova um dispositivo com **Remover** para liberar a vaga dele; um dispositivo removido precisa ser pareado de novo antes de poder se reconectar.

## O firewall

Na primeira vez que o complemento inicia, o Windows e o macOS perguntam se permitem que o BookDB aceite conexões de entrada. Você precisa permitir, ou os dispositivos não conseguirão alcançar o BookDB. **No Linux nada pergunta**: se houver um firewall em execução, você mesmo terá de abrir as portas.

- **Windows** — permita o BookDB em **redes privadas** (sua rede doméstica ou de escritório). Você **não** precisa permitir em redes públicas. Se você dispensou o aviso ou clicou em *Cancelar*, nenhuma conexão passará; permita novamente em **Segurança do Windows › Firewall e proteção de rede › Permitir um aplicativo pelo firewall**, marque a caixa **Privada** do BookDB, ou exclua a regra de bloqueio do BookDB para que o aviso reapareça na próxima vez.
- **macOS** — permita as conexões de entrada para o BookDB quando solicitado. Você pode revisar isso depois em **Ajustes do Sistema › Rede › Firewall**.
- **Linux** — nenhuma pergunta aparece. Se o `ufw` ou o `firewalld` estiver em execução, ele bloqueia portas de entrada por padrão, e a porta do complemento (**7443** por padrão) precisa estar aberta para **TCP e UDP**: o TCP leva a conexão e o UDP é como um dispositivo encontra este computador novamente quando o endereço muda. Abrir apenas o TCP deixa o pareamento e a navegação funcionando enquanto a redescoberta falha em silêncio — algo geralmente percebido muito depois, como uma falha intermitente após uma mudança de endereço. Para `ufw`: `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` + `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto udp`, substituindo `192.168.1.0/24` pela sua própria rede. Para `firewalld`: `sudo firewall-cmd --permanent --add-port=7443/tcp --add-port=7443/udp` + `sudo firewall-cmd --reload`. Se você mudou a porta do complemento, use a sua porta nos dois comandos.

O complemento só escuta na sua rede local e apenas enquanto está ativado.

## Se um dispositivo não conseguir conectar

- **Ambos na mesma rede?** O dispositivo e este computador precisam estar na mesma Wi-Fi. Uma rede de «convidados» costuma ser isolada da principal e não funcionará.
- **Firewall** — confira as notas sobre o firewall acima; uma porta bloqueada é a causa mais comum.
- **Complemento em execução?** A linha de Status na aba Configurações ▸ Complementar precisa mostrá-lo como em execução. Se ele não conseguiu iniciar, mude a porta e salve de novo.
- **Ainda pareado?** Se o dispositivo foi removido da lista de dispositivos, ou se já faz muito tempo, pareie-o de novo com um código novo.
- **Suspensão** — se este computador entrou em suspensão, o complemento é retomado ao despertar; o dispositivo se reconecta sozinho assim que ambos estiverem despertos e na rede.

## O que o dispositivo pode e não pode fazer

Um dispositivo pareado pode digitalizar ISBNs, tirar fotos de capa e de página, enviá-las para catalogação e navegar pela biblioteca para verificar se você já tem um livro. Ele trabalha na biblioteca que estiver aberta no momento no BookDB — incluindo uma biblioteca armazenada em um servidor de banco de dados remoto. Ele não pode alterar as configurações do BookDB nem remover outros dispositivos; isso permanece neste computador.
