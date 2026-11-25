# SP2 UI Editor: guia para quem nunca programou

Este programa muda a interface do SimplePlanes 2 (botões, textos, cores, janelas) com o jogo aberto. Você clica no que quer mudar e vê o resultado na hora.

**Dá para estragar o jogo?** Os arquivos do jogo nunca são tocados. Tudo pode ser desfeito, e nada fica guardado até você salvar. Veja "Deu errado, e agora?" no fim.

**O botão (!)** aparece ao lado de quase tudo no programa. Passe o mouse ou clique nele para ler o que aquilo é e como preencher. O botão **Ajuda**, no alto, tem um resumo deste guia.

## 1. Instalar (só uma vez)

Você precisa do BepInEx 5 (x64) no jogo. É ele que permite usar mods. Se já usa algum mod de SP2 com BepInEx, está pronto.

1. Feche o jogo.
2. Copie `plugin\SP2UIEditor.dll` para a pasta `BepInEx\plugins`, dentro da pasta do jogo. (No Steam: botão direito no jogo > Gerenciar > Explorar arquivos locais.)
3. Abra o jogo.

**Só quer usar a interface feita por outra pessoa?** Pule para a parte 6.

## 2. Conectar

1. Com o jogo aberto, abra `editor\SP2UIEditor.exe`.
2. O jogo pergunta se o programa pode conectar. Clique em **Okay**.

Para o jogo não perguntar mais: abra `BepInEx\config\com.sp2uieditor.cfg` no Bloco de Notas, troque para `Enabled = true`, salve e reabra o jogo.

## 3. Sua primeira mudança

1. No programa, clique em **Escolher no jogo**.
2. Vá ao jogo e clique num botão. Ele fica marcado.
3. Volte ao programa. À direita aparecem as características dele.
4. Em **Cor**, clique no quadradinho colorido e escolha outra cor.
5. Olhe o jogo: a cor mudou.
6. Clique em **Desfazer**. Voltou ao que era.

## 4. A janela do programa

| Parte | Para que serve |
|---|---|
| Esquerda: a lista | tudo o que está na tela do jogo; o que está mais para dentro fica dentro do item de cima |
| Meio: a imagem | o que o jogo está mostrando; clique nela para escolher |
| Direita: as características | o que você pode mudar no item escolhido |
| Alto: os botões | desfazer, janelas suas, pacotes, salvar |

Palavras que aparecem no programa:

| Palavra | Significa |
|---|---|
| Elemento | qualquer coisa na tela: um botão, um texto, uma figura, uma caixa |
| Propriedade | uma característica do elemento: cor, largura, texto |
| Caixa, lista, linha | elementos que guardam outros e os arrumam |
| Âncora | o canto ou lado da tela em que o elemento fica preso |
| Classe de estilo | uma aparência pronta; todos que usam a mesma ficam parecidos |
| Constante | um valor com nome, como "Cor principal", usado em muitos lugares |
| Modelo | uma peça pronta que você pode adicionar |
| Painel | uma janela sua dentro do jogo |
| Pacote | o arquivo que guarda suas mudanças |
| Método | um comando interno do jogo que um botão seu pode chamar |

Como preencher os valores:

| Tipo | Exemplo |
|---|---|
| Número | `120` (tamanhos são em pontos da tela; use ponto para decimais: `0.5`) |
| Cor | clique no quadradinho, ou escreva `#FF0000` (vermelho) |
| Posição | `10 -20`: primeiro para o lado, depois para cima (negativo desce) |
| Margens | `5 10 5 10`: cima, direita, baixo, esquerda |

Valor errado? O programa avisa e não aplica.

## 5. Receitas

**Trocar o que está escrito num botão**
1. Escolha o botão.
2. Se à direita o nome do tipo for "Texto", vá ao passo 4.
3. Em **Textos e partes**, clique no texto.
4. Mude **Texto** e aperte Enter.

**Esconder algo.** Escolha e clique em **Esconder**. Para voltar, **Mostrar** ou **Desfazer**.

**Mudar a cor de todos os botões do mesmo tipo.** Escolha um deles, clique em **Estilos gerais** e mude a cor. Vale para todos de uma vez.

**Mudar a ordem.** Escolha e use **Subir** e **Descer**.

**Pôr algo novo.** Escolha um lugar e clique em **Adicionar: Dentro** (no fim daquele lugar) ou **Adicionar: Depois** (logo depois do item). Escolha o que pôr; cada opção diz o que é.

**Fazer uma janela sua**
1. **Painéis** > escreva um nome > **Novo painel**.
2. A janela abre no jogo. Use **Adicionar: Dentro** para cada item.

**Fazer um botão que abre a sua janela**
1. Durante o voo, em **Ir para um lugar...**, escolha **Menu de voo**.
2. **Adicionar: Dentro** > **Botão**.
3. Em **O que faz**, escolha **Abrir ou fechar painel**, escolha a janela e clique em **Aplicar**.

**Controlar uma opção de um mod**
1. Na sua janela, adicione **Liga/desliga** (sim ou não) ou **Controle deslizante** (número).
2. Em **O que faz**, escolha **Configuração de mod**, procure a opção e clique em **Aplicar**.

**Controlar uma peça da sua nave**
1. Adicione um botão, liga/desliga ou controle deslizante.
2. Em **O que faz**, escolha **Variável da nave**, dê um nome (por exemplo `MeuFarol`) e clique em **Aplicar**.
3. Na peça, use esse nome na expressão do controlador (Funky Trees).

**Um botão que chama um comando do jogo.** Em **O que faz**, **Método do jogo**. Cada comando tem uma explicação; as marcadas como "descrição automática" foram feitas só a partir do nome. No primeiro clique o jogo pede a sua permissão.

## 6. Guardar e compartilhar

**Guardar.** Em **Pacotes**, escreva um nome e clique em **Novo pacote** (só na primeira vez). Depois, **Salvar**. As mudanças voltam sozinhas quando o jogo abrir.

**Mandar para um amigo.** A janela **Pacotes** mostra, no fim, onde fica a pasta. Envie o arquivo `.sp2ui` com o nome do seu pacote.

**Usar o de outra pessoa.** Instale (parte 1, sem precisar do programa do editor), abra o jogo uma vez, feche, e ponha o arquivo `.sp2ui` em `BepInEx\config\SP2UIEditor\packages`.

**Ligar e desligar.** No jogo, **Ctrl+Shift+U** abre a lista de pacotes. Cada um diz em que telas mexe: se ele só muda o Designer, você não verá diferença no voo.

**Cuidado:** se um pacote quiser chamar um comando do jogo, o jogo pergunta antes. Só aceite de quem você confia.

## 7. Deu errado, e agora?

| O que aconteceu | O que fazer |
|---|---|
| não gostei da última mudança | **Desfazer** (Ctrl+Z) quantas vezes precisar |
| quero tirar uma mudança já salva | **Pacotes**, e o **✕** ao lado dela |
| a tela ficou estranha ou sumiu algo | **Ctrl+Shift+U** e desligue o pacote |
| nem isso funciona | feche o jogo e tire o arquivo `.sp2ui` da pasta de pacotes |
| quero tudo como antes | feche o jogo e apague `SP2UIEditor.dll` de `BepInEx\plugins` |
| a janela fica em "Esperando o jogo" | veja se o jogo mostra a pergunta de conexão |
| não acho uma propriedade | abra **Avançado** e escreva parte do nome |
| mudei e nada aconteceu | quem está em volta pode mandar no tamanho (uma lista arruma seus itens): mude a caixa de fora, pelo **caminho** no alto à direita |
| uma mudança sumiu depois de atualizar o jogo | **Pacotes** mostra "elemento não existe mais"; refaça |
| não diz de que arquivo a tela veio | feche e abra essa tela no jogo |
| tirar permissões dadas a comandos | com o jogo fechado, apague `BepInEx\config\SP2UIEditor\trust.json` |

## 8. Opcional: pedir ao Claude

Se você usa o Claude Code, registre o conector uma vez:

    claude mcp add --scope user sp2ui -- C:\caminho\mcp\SP2UI.Mcp.exe

Aí pode pedir, por exemplo, "deixe os botões do menu mais largos". Ele só muda algo com **Claude pode editar** marcado na janela, e nunca pode dar permissão a um comando do jogo.

## 9. Para quem faz mods

**Pacotes** > **Exportar XML e C#** gera o código da interface para usar no seu mod.
