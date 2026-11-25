# SP2 UI Editor

Edita a interface do SimplePlanes 2 (Juicy UI) com o jogo aberto. Fases 1 a 6 do plano: ver, editar ao vivo e salvar em pacotes (`BepInEx/config/SP2UIEditor/packages/*.sp2ui`).

| Pasta | O que é |
|---|---|
| `Core/` | Marcas de origem (`_src`), o que é permitido escrever, histórico. Sem Unity |
| `Plugin/` | Plugin BepInEx: patches, árvore, seleção, edição, captura, servidor em `127.0.0.1` e a página |
| `Mcp/` | Conector do Claude (MCP por stdio) |
| `Desktop/` | Janela do editor (WebView2) |
| `Tests/` | Testes do núcleo; `fake/fake_game.py` imita o jogo para testar a página e o conector |

## Compilar

    dotnet build -c Release Plugin -p:GameDir=C:\Geral\SP2_ALT
    dotnet publish -c Release Mcp -o publish\mcp
    dotnet publish -c Release Desktop -o publish\editor
    dotnet run -c Release --project Tests -- "C:\Geral\SP2_ALT\SimplePlanes 2_Data\resources.assets"

## Usar

1. Copie `SP2UIEditor.dll` para `BepInEx\scripts` (F6) ou `BepInEx\plugins`.
2. Abra `publish\editor\SP2UIEditor.exe`.
3. "Escolher no jogo", clique num elemento e edite à direita. Ctrl+Z desfaz.
4. Claude: `claude mcp add sp2ui -- <pasta>\publish\mcp\SP2UI.Mcp.exe`. Ele só edita com "Claude pode editar" marcado.

## Segurança

O servidor só escuta em `127.0.0.1`, confere Host e Origin e exige uma chave nova a cada partida (em `%LOCALAPPDATA%\SP2UIEditor\session.json`).
`script`, eventos (`onClick`...) e expressões `{ }` são recusados. Elementos do jogo nunca são destruídos, só escondidos.
Descarregar o plugin (F6) desfaz as mudanças.

## Estado

Testado: núcleo nos 79 XML do jogo, página e conector com o servidor falso. Testado no jogo (Designer, `Tests/live_test.py`): seleção, busca, captura, editar, esconder, mover, adicionar, remover, desfazer e refazer. **Falta**: voo, menu principal, F6 com mudanças feitas, recusa de valores inválidos (só fora do jogo).
Limites: uma tela montada antes de o plugin carregar não mostra o arquivo de origem (reabra a tela). A página consulta o jogo a cada 0,7 s em vez de usar *long-poll*.
Classes e itens removidos podem não voltar exatamente ao desfazer (a lista de mudanças avisa).

Pacotes (fase 3): cada edição guarda a identidade do elemento e é refeita quando o jogo monta a tela (um quadro depois). Elemento que não existe mais vira órfão; nunca muda outro. Testado no Designer com F6 e reinício (`Tests/live_packages.py`). Não feito: "manter o meu" para textos que o jogo reescreve.

Painéis e ações (fase 4): um painel é um layout servido em `SP2UI/<uid>`; ações `panel`, `element`, `link` (só http/https, com confirmação), `config`, `variable`, `mod` (`SP2UIEditor.UiActions.Register`). Nenhuma executa código do pacote. Testado no voo (`Tests/live_panel.py`), sem cliques reais.

Fase 5: estilos e constantes compartilhados (`Plugin/Styles.cs`), métodos do jogo (`Core/Calls.cs` diz quais; só rodam depois que o jogador aprova aquela chamada num diálogo do jogo, no primeiro clique; aprovação por chamada, em `trust.json`) e exportação (`Core/Exporter.cs`, em `BepInEx/config/SP2UIEditor/export`). Estilos, constantes, recusas e exportação testados no voo (`Tests/live_phase5.py`); aprovação e chamada de método sem teste. O C# exportado compila; não rodou no jogo. Ações de configuração, variável, elemento, mod e método não são exportadas (o arquivo lista).

Fase 6: `python make-dist.py <pasta do jogo>` gera `dist/SP2-UI-Editor-<versão>.zip` (plugin, editor e conector em um arquivo cada, guias `GUIA.md`/`GUIDE.md`). O servidor vem desligado: o jogo pergunta quando o programa do editor abre (`Plugin/Player.cs`); Ctrl+Shift+U abre a lista de pacotes. Só compilado.
