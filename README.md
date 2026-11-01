# Penumbra

Widget para Windows 11 que escurece ou desliga os monitores secundários enquanto você joga e volta ao brilho padrão quando o jogo fecha.

[Site](https://ferbdev.github.io/penumbra/) · [Baixar Penumbra.exe](https://github.com/ferbdev/penumbra/releases/latest/download/Penumbra.exe)

## Como funciona

- Fica na bandeja do Windows. Um clique no ícone mostra ou esconde o widget.
- Detecta um app em tela cheia no monitor principal e aplica o preset selecionado.
- Controla o brilho real dos monitores via DDC/CI.
- Cada ajuste nos cartões vira o brilho padrão daquele monitor, aplicado ao sair do jogo.
- Monitores ligados por duas entradas são reconhecidos pelo serial do EDID e nunca recebem o preset no lugar do principal.

## Desenvolvimento

Requer o SDK do .NET 8.

```bash
dotnet test -c Release
```

```bash
dotnet publish src/Penumbra/Penumbra.csproj -c Release -o publish
```

Diagnóstico dos monitores:

```bash
publish\Penumbra.exe --list
```

Os dados ficam em `%AppData%\Penumbra`.
