# Complementar

O complemento do BookDB permite catalogar livros a partir de um **telefone ou tablet** na mesma rede local. Digitaliza um código de barras ISBN (e, opcionalmente, fotografa a capa) no dispositivo, e o livro flui para a biblioteca em execução neste computador. Nada sai da sua rede: o dispositivo comunica diretamente com o BookDB através do seu Wi-Fi, nunca pela internet nem por qualquer servidor externo.

O complemento está **desativado por predefinição**. Ativa-o, emparelha cada dispositivo uma vez e, a partir daí, o dispositivo volta a ligar-se sozinho sempre que ambos estiverem na mesma rede.

## Ativar o complemento

Abra **Ferramentas › Configurações › Complementar** e assinale **Ativar complemento**. A definição entra em vigor quando prime **Guardar** — e não no momento em que assinala a caixa —, para que possa ajustar primeiro a porta e as opções de captura e aplicar tudo de uma vez.

Depois de em execução, a linha de **Estado** mostra que o complemento está à escuta. Se não conseguir iniciar — normalmente porque a porta já está em uso —, a caixa de diálogo permanece aberta no separador Complementar e indica o motivo, e a definição permanece ativada para que possa mudar a porta e tentar novamente.

Pode deixar o complemento ativado entre sessões; inicia automaticamente com o BookDB sempre que a definição estiver ativada.

### Definições

- **Porta** — a porta de rede em que o complemento escuta (predefinição **7443**). Altere-a apenas se outro programa já usar essa porta. Se a alterar, não é necessário voltar a emparelhar, mas o dispositivo pode demorar um instante a redescobrir o BookDB na nova porta.
- **Tamanho máximo da imagem** e **qualidade JPEG** — como as fotos de capa e de página são redimensionadas e comprimidas antes de serem armazenadas. Valores mais baixos poupam espaço; valores mais altos mantêm mais detalhe. Isto aplica-se às fotos tiradas no dispositivo.

## Emparelhar um dispositivo

Um dispositivo tem de ser emparelhado uma vez antes de poder enviar livros. O emparelhamento troca um conjunto de certificados de segurança para que apenas os dispositivos que **você** aprovou possam ligar-se, e tudo o que passa entre eles é cifrado.

1. Certifique-se de que o complemento está ativado e em execução, e de que o dispositivo está na **mesma rede Wi-Fi** que este computador.
2. Abra **Ferramentas › Manutenção › Dispositivos** e prima **Mostrar código de emparelhamento…**. (Há também um botão para gerir dispositivos no separador Configurações ▸ Complementar que abre o mesmo local.)
3. Uma janela mostra um **código QR**. Na aplicação BookDB no dispositivo, escolha emparelhar e digitalize o código.
4. Quando o dispositivo se liga, a janela confirma o emparelhamento. Pode emparelhar outro dispositivo ou fechar a janela.

O código de emparelhamento é **de utilização única e de curta duração** — renova-se sozinho a cada dois minutos, aproximadamente, e só pode ser usado uma vez. Se um código expirar antes de o digitalizar, aparece outro automaticamente. Se o endereço de rede escolhido automaticamente não for aquele que o dispositivo consegue alcançar, escolha outro na lista pendente antes de digitalizar.

Pode emparelhar até **cinco dispositivos**. Cada um aparece na lista de dispositivos com o nome que recebeu, quando foi emparelhado e quando foi usado pela última vez. Remova um dispositivo com **Remover** para libertar o seu lugar; um dispositivo removido tem de ser emparelhado novamente antes de poder voltar a ligar-se.

## A firewall

Na primeira vez que o complemento inicia, o Windows e o macOS perguntam se permitem que o BookDB aceite ligações de entrada. Tem de permitir, ou os dispositivos não conseguirão alcançar o BookDB. **No Linux nada pergunta**: se houver uma firewall em execução, terá de abrir as portas você mesmo.

- **Windows** — permita o BookDB em **redes privadas** (a sua rede doméstica ou de escritório). **Não** precisa de o permitir em redes públicas. Se dispensou o aviso ou clicou em *Cancelar*, nenhuma ligação passará; permita-o novamente em **Segurança do Windows › Firewall e proteção de rede › Permitir uma aplicação através da firewall**, assinale a caixa **Privada** do BookDB, ou elimine a regra de bloqueio do BookDB para que o aviso reapareça da próxima vez.
- **macOS** — permita as ligações de entrada para o BookDB quando lhe for pedido. Pode rever isto mais tarde em **Definições do Sistema › Rede › Firewall**.
- **Linux** — não aparece qualquer pergunta. Se o `ufw` ou o `firewalld` estiver em execução, bloqueia as portas de entrada por predefinição, e a porta do complemento (**7443** por predefinição) tem de estar aberta para **TCP e UDP**: o TCP leva a ligação e o UDP é como um dispositivo volta a encontrar este computador quando o endereço muda. Abrir apenas o TCP deixa o emparelhamento e a navegação a funcionar enquanto a redescoberta falha em silêncio — algo normalmente notado muito mais tarde, como uma falha intermitente após uma mudança de endereço. Para `ufw`: `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto tcp` + `sudo ufw allow from 192.168.1.0/24 to any port 7443 proto udp`, substituindo `192.168.1.0/24` pela sua própria rede. Para `firewalld`: `sudo firewall-cmd --permanent --add-port=7443/tcp --add-port=7443/udp` + `sudo firewall-cmd --reload`. Se mudou a porta do complemento, use a sua porta em ambos os comandos.

O complemento só escuta na sua rede local e apenas enquanto está ativado.

## Se um dispositivo não conseguir ligar-se

- **Ambos na mesma rede?** O dispositivo e este computador têm de estar na mesma Wi-Fi. Uma rede de «convidados» costuma estar isolada da principal e não funcionará.
- **Firewall** — verifique as notas sobre a firewall acima; uma porta bloqueada é a causa mais comum.
- **Complemento em execução?** A linha de Estado no separador Configurações ▸ Complementar tem de o mostrar como em execução. Se não conseguiu iniciar, mude a porta e guarde novamente.
- **Ainda emparelhado?** Se o dispositivo foi removido da lista de dispositivos, ou se já passou muito tempo, emparelhe-o novamente com um código novo.
- **Suspensão** — se este computador entrou em suspensão, o complemento retoma ao despertar; o dispositivo volta a ligar-se sozinho assim que ambos estiverem despertos e na rede.

## O que o dispositivo pode e não pode fazer

Um dispositivo emparelhado pode digitalizar ISBNs, tirar fotos de capa e de página, enviá-las para catalogação e navegar pela biblioteca para verificar se já tem um livro. Trabalha sobre a biblioteca que estiver aberta no momento no BookDB — incluindo uma biblioteca armazenada num servidor de base de dados remoto. Não pode alterar as definições do BookDB nem remover outros dispositivos; isso permanece neste computador.
