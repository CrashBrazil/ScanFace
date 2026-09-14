# Modelo de ameaça

## Ativos protegidos

- senhas, usuários, sites e notas do cofre;
- chave aleatória do cofre;
- senha mestra;
- token da API de sincronização;
- integridade e autenticidade de cada credencial cifrada.

## Ameaças consideradas

| Ameaça | Mitigação |
|---|---|
| Cópia do `vault.db` com o aplicativo fechado | conteúdo cifrado com AES-256-GCM; chave protegida por Argon2id e salt aleatório |
| Força bruta offline da senha mestra | Argon2id com 64 MiB, 3 iterações e paralelismo 4; requisito mínimo de 12 caracteres |
| Alteração de ciphertext, nonce ou tag | autenticação do AES-GCM rejeita o registro adulterado |
| Troca de ciphertext entre registros | identificador do item incluído como AAD |
| Vazamento ou invasão da API | servidor recebe somente envelope e registros cifrados |
| Sobrescrita concorrente na API | controle de versão e `If-Match`; conflito preserva os dois lados |
| Cópia do backup | mesmo material cifrado do cofre; envelope Hello local é excluído |
| Leitura casual da área de transferência | limpeza automática após 30 segundos |
| Computador deixado aberto | bloqueio automático após cinco minutos |

## Limites conhecidos

O Windows Hello escolhe o método permitido pelo sistema. Mesmo em um computador com câmera facial, o Windows pode oferecer PIN ou impressão digital. O ScanFace não recebe nem armazena o template biométrico.

O desbloqueio Hello usa `UserConsentVerifier` como confirmação e DPAPI `CurrentUser` para proteger a chave no dispositivo. Isso protege contra cópia do banco para outra conta ou computador, mas não contra malware já executando com os mesmos privilégios do usuário nem contra um administrador que controle a sessão. A senha mestra também não protege o conteúdo enquanto o cofre está desbloqueado e a chave está na memória.

A API observa o identificador do cofre, tamanho aproximado do snapshot, versão e horário das atualizações. Ela não vê os campos das credenciais. HTTPS é obrigatório fora de desenvolvimento para proteger o token e reduzir vazamento de metadados na rede.

Este MVP não oferece extensão de navegador, preenchimento automático, compartilhamento, aplicação móvel, recuperação por servidor ou verificação de hardware contra ataques físicos avançados.

## Perda da senha mestra

Se o Windows Hello estiver ativo, o usuário pode continuar entrando naquele perfil do dispositivo e definir uma nova senha mestra nas configurações. Se perder simultaneamente a senha mestra e o acesso ao Hello/DPAPI daquele perfil, não existe recuperação: a API não possui a chave. Backups antigos continuam exigindo a senha que os protegia no momento da exportação.

## Recomendações operacionais

- Use uma senha mestra longa e exclusiva.
- Mantenha Windows, firmware e drivers da câmera atualizados.
- Ative BitLocker para proteção adicional do disco.
- Exporte backups cifrados após mudanças importantes e guarde-os em local separado.
- Gere um token de sincronização longo, use HTTPS e não publique o token em arquivos do repositório.
- Bloqueie a sessão do Windows ao se afastar.
