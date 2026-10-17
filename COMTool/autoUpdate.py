try:
    import version
    import parameters
except ImportError:
    from COMTool import version, parameters
import platform

log = parameters.log

class AutoUpdate:
    # This fork has its own release channel. Do not point users at upstream
    # releases, otherwise an update would replace the custom port handling.
    updateUrl = "https://github.com/ph-rui/COMTool/releases"
    releaseApiUrl = "https://api.github.com/repos/ph-rui/COMTool/releases"
    def detectNewVersion(self):
        need, v = self.checkUpdate_github()
        return need, v
    
    def checkUpdate_github(self):
        import requests, json
        latest = version.Version()
        try:
            page = requests.get(self.releaseApiUrl)
            if page.status_code != 200:
                log.i("request {} fail, check update fail!".format(self.releaseApiUrl))
                return False, None
            releases = json.loads(page.content)
            releasesInfo = []
            for release in releases:
                if release["prerelease"] or release["draft"]:
                    continue
                tag = release["tag_name"]
                name = release["name"]
                body = release["body"]
                ver = self.decodeTag(tag, name, body)
                releasesInfo.append([ver, ver.major * 100 + ver.minor * 10 + ver.dev])
            releasesInfo = sorted(releasesInfo, key=lambda x:x[1], reverse=True)
            latest = releasesInfo[0][0]
            if self.needUpdate(latest):
                return True, latest
        except Exception as e:
            import traceback
            traceback.print_exc()
            return False, None
        log.i("Already latest version!")
        return False, latest

    def decodeTag(self, tag, name, body):
        # v1.7.9
        tag = tag[1:].split(".")
        return version.Version(int(tag[0]), int(tag[1]), int(tag[2]) if len(tag) > 2 else 0, name, body)

    def needUpdate(self, ver):
        if ver.major * 10 + ver.minor > version.major * 10 + version.minor:
            return True
        return False

    def OpenBrowser(self):
        import webbrowser
        webbrowser.open(self.updateUrl, new=0, autoraise=True)
        return

if __name__ == "__main__":
    update = AutoUpdate()
    needUpdate, latest = update.detectNewVersion()
    print(needUpdate, latest)
