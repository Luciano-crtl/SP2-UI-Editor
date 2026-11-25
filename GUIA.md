# SP2 UI Editor: guia

Muda a interface do SimplePlanes 2 sem programar, com o jogo aberto. Precisa do BepInEx 5 (x64) instalado no jogo.

## Só usar pacotes de outras pessoas

1. Feche o jogo e copie `plugin\SP2UIEditor.dll` para `BepInEx\plugins`.
2. Abra o jogo uma vez e feche.
3. Copie os arquivos `.sp2ui` para `BepInEx\config\SP2UIEditor\packages`.
4. No jogo, **Ctrl+Shift+U** abre a lista para ligar e desligar cada pacote.

Não é preciso ter o programa do editor, e nenhum servidor é aberto. Se um botão de um pacote quiser chamar uma função do jogo, o jogo pergunta antes: só aceite de quem você confia.

## Editar

1. Faça o passo 1 acima e abra o jogo.
2. Abra `editor\SP2UIEditor.exe`. O jogo pergunta se pode conectar: clique em **Okay**.
   Para não perguntar mais, ponha `Enabled = true` em `BepInEx\config\com.luciano.sp2uieditor.cfg`.
3. **Escolher no jogo**, clique num elemento e mude as propriedades à direita. **Ctrl+Z** na janela desfaz.
4. **Pacotes** > dê um nome > **Novo pacote**, e depois **Salvar**.

| Quero... | Faça |
|---|---|
| mudar um texto | selecione a parte de texto, em "Textos e partes" |
| pôr algo novo | **Adicionar: Dentro** ou **Adicionar: Depois** |
| fazer uma janela | **Painéis** > nome > **Novo painel**, depois **Adicionar: Dentro** |
| abrir a janela por um botão | adicione um botão, e em **O que faz** escolha "Abrir ou fechar painel" |
| ligar um controle a um mod | adicione um liga/desliga ou controle deslizante, e em **O que faz** escolha "Configuração de mod" |
| mudar a cor de todos os botões | **Estilos gerais** |
| mandar para um amigo | envie o arquivo da pasta de pacotes |
| usar num mod seu | **Pacotes** > **Exportar XML e C#** |

Se uma tela já estava aberta quando o plugin carregou, feche e abra a tela para ver de que arquivo ela veio.

## Claude

    claude mcp add --scope user sp2ui -- C:\caminho\mcp\SP2UI.Mcp.exe

Ele vê a interface e pode selecionar elementos. Só muda algo com **Claude pode editar** marcado na janela, e nunca pode aprovar uma função do jogo.

## Problemas

| O que acontece | O que fazer |
|---|---|
| a janela fica em "Esperando o jogo" | veja se o jogo mostra a pergunta; procure `[UIEDIT]` em `BepInEx\LogOutput.log` |
| uma edição sumiu depois de atualizar o jogo | **Pacotes** mostra "elemento não existe mais"; refaça a edição |
| algo ficou estranho | desligue o pacote (Ctrl+Shift+U) ou tire o arquivo da pasta |
| revogar permissões de funções | com o jogo fechado, apague `BepInEx\config\SP2UIEditor\trust.json` |
