function Find-SerialHidBootloaderPort {
    param([string[]]$LocationPaths, [object[]]$Candidates)

    $atTarget = @($Candidates | Where-Object {
        $_.Status -eq 'OK' -and
        @($_.LocationPaths | Where-Object { $_ -in $LocationPaths }).Count -gt 0
    })
    # 通常動作時のUSB名はsketchが決める。書込み側は実測済みのAVR109識別子を使う。
    $supportedPattern = '^USB\\(?:VID_1B4F&PID_9205|VID_2341&PID_0036)\\'
    $unknown = @($atTarget | Where-Object {
        $_.InstanceId -notmatch $supportedPattern -and
        $_.InstanceId -notmatch '^USB\\VID_1B4F&PID_9206(?:&MI_\d+)?\\'
    })
    if ($unknown.Count -gt 0) {
        throw [NotSupportedException]::new("対象の接続口に未対応の書込み機器が現れました: $($unknown.InstanceId -join ', ')")
    }
    $bootPorts = @($atTarget | Where-Object { $_.InstanceId -match $supportedPattern })
    if ($bootPorts.Count -gt 1) {
        throw [InvalidOperationException]::new('同じ物理接続のbootloaderが複数あります。')
    }
    if ($bootPorts.Count -eq 1 -and $bootPorts[0].FriendlyName -match '\((COM\d+)\)$') {
        return $Matches[1]
    }
    return $null
}

Export-ModuleMember -Function Find-SerialHidBootloaderPort
