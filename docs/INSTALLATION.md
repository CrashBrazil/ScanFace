# Instalação, atualização e portabilidade

## Requisitos

- Windows 10 versão 2004 ou posterior, ou Windows 11;
- computador com arquitetura x64;
- Windows Hello configurado no sistema para desbloqueio por rosto, impressão digital ou PIN.

O ScanFace é autocontido. O computador não precisa ter o .NET instalado.

## Instalador recomendado

Baixe `ScanFace-Setup.exe` na [release mais recente](https://github.com/CrashBrazil/ScanFace/releases/latest) e execute-o. A instalação ocorre no perfil atual do Windows, não pede acesso de administrador e oferece:

- atalho no menu Iniciar;
- atalho opcional na área de trabalho;
- entrada própria para desinstalação nas Configurações do Windows;
- opção de abrir o ScanFace ao concluir.

## Versão portátil

`ScanFace.exe` é um executável único e autocontido. Ele pode ser colocado em qualquer pasta gravável, pendrive ou área de trabalho e executado diretamente. O ZIP contém a mesma aplicação em um pacote completo.

“Portátil” se refere ao programa. Por segurança, o cofre não fica ao lado do EXE: ele é armazenado no perfil do Windows em `%LOCALAPPDATA%\ScanFace\vault.db`.

## Atualizar ou desinstalar

Feche o ScanFace e execute a versão mais nova do instalador. O aplicativo é substituído e o cofre local permanece no mesmo lugar. A desinstalação também preserva o cofre e os backups do usuário.

A versão 1.1.0 abre cofres 1.0.x diretamente e adiciona o armazenamento de pastas de forma automática. Nenhuma exportação prévia é necessária, embora manter um backup cifrado recente seja recomendado.

## Usar em outro computador

Copiar apenas o EXE não transfere o cofre. No computador antigo, use **Exportar backup**. No novo computador, abra o ScanFace, escolha **Restaurar backup cifrado** e informe a senha mestra usada naquele backup.

O Windows Hello é vinculado ao perfil e ao dispositivo. Depois da restauração, confirme o Windows Hello do novo computador para criar a proteção local correspondente. A senha mestra continua necessária para restaurar backups em outra máquina.

## Verificar o download

Cada release inclui `SHA256SUMS.txt`. Calcule o hash do arquivo baixado e compare com o valor publicado:

```powershell
Get-FileHash .\ScanFace-Setup.exe -Algorithm SHA256
```
