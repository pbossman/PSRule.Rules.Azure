#
# Unit tests for PSRule rule quality
#

[CmdletBinding()]
param (

)

# Setup error handling
$ErrorActionPreference = 'Stop';
Set-StrictMode -Version latest;

if ($Env:SYSTEM_DEBUG -eq 'true') {
    $VerbosePreference = 'Continue';
}

# Setup tests paths
$rootPath = $PWD;
Import-Module (Join-Path -Path $rootPath -ChildPath out/modules/PSRule.Rules.Azure) -Force;

Describe 'Rule quality' {
    Context 'Metadata' {
        $result = Get-PSRule -Module PSRule.Rules.Azure -WarningAction Ignore;

        foreach ($rule in $result) {
            It $rule.RuleName {
                $rule.Description | Should -Not -BeNullOrEmpty;
                $rule.Tag.severity | Should -Not -BeNullOrEmpty;
                $rule.Tag.category | Should -Not -BeNullOrEmpty;
            }
        }
    }
}
