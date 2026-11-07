# The rules of PSScriptAnalyzer for deploy/reliquary-update.ps1.
# The script is a command line tool, not a module, so these rules do not apply:
# - PSAvoidUsingWriteHost: the script writes its messages to the console of the user.
# - PSUseShouldProcessForStateChangingFunctions: the internal functions do not take -WhatIf.
# - PSUseSingularNouns: the names of the internal functions follow the words of the POSIX script.
@{
    Severity     = @('Error', 'Warning')
    ExcludeRules = @(
        'PSAvoidUsingWriteHost',
        'PSUseShouldProcessForStateChangingFunctions',
        'PSUseSingularNouns'
    )
}
