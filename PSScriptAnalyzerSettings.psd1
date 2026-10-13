@{
    # Used by CI (.github/workflows/ci.yml) and by anyone running
    #     Invoke-ScriptAnalyzer -Path . -Recurse -Settings ./PSScriptAnalyzerSettings.psd1
    Severity     = @('Error', 'Warning')

    ExcludeRules = @(
        # These are installer scripts whose whole output is coloured progress
        # for a person watching a console. Write-Host is the right tool.
        'PSAvoidUsingWriteHost',

        # The helper functions are private to one script (Say, Good, Warn,
        # Set-UserPath...). They are not cmdlets and do not need -WhatIf.
        'PSUseShouldProcessForStateChangingFunctions',
        'PSUseApprovedVerbs',

        # The scripts are deliberately plain ASCII, so there is nothing for a
        # byte-order mark to disambiguate.
        'PSUseBOMForUnicodeEncodedFile'
    )
}
