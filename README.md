# SP2 UI Editor

Edita a interface do SimplePlanes 2 (Juicy UI) com o jogo aberto. Fases 1 e 2 do plano: ver e editar ao vivo. **Ainda não salva**: as mudanças duram até a tela fechar.

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
