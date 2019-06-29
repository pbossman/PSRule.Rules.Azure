
Document 'Azure' {
    Title 'Azure rules'

    Get-PSRule -WarningAction SilentlyContinue | Table -Property @{ Name = 'RuleName'; Expression = {
        "[$($_.RuleName)]($($_.RuleName).md)"
    }}, Description
}