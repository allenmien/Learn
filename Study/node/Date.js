
function fnonblur(str) {
        var reg = /^([一二三四五六七八九][(○|〇)一二三四五六七八九]+)年([一二三四五六七八九]|十[一二]{0,1})月([一二三四五六七八九]|(十|二十)[一二三四五六七八九]{0,1}|三十(|一))日$/;
		//var str = "  二○○四年四月二十九日";
        var values = str.replace(/^\s+|\s+$/g, '');
		var values = values.replace('○','〇');
		var values = values.replace('○','〇');
		var values = values.replace('○','〇');
		
        var matches = values.match(reg);
        var date = [];
        var temp;
        if(matches) {
            for(var i = 1; i <= 3; i++) {
                temp = trans(matches[i]);
                if(temp) {
                    date.push(temp);
                }
            }
            if(date.length == 3) {
                strs = date.join('-');
            }
        }else{
			strs = str.replace('年','-');
			strs = strs.replace('月','-');
			strs = strs.replace('日','');
			
		}
		console.log(strs);
        
    }
    
    function trans(str) {
        var map = '〇一二三四五六七八九十';
        return     str.length == 0? '' : 
                str.length == 1? '0' + map.indexOf(str) : 
                str.length == 2? (/^十/.test(str)? '1' + map.indexOf(str.substr(1)) : map.indexOf(str.substr(0,1)) + '0') : 
                str.length == 3? map.indexOf(str.substr(0,1)).toString() + map.indexOf(str.substr(2,1)).toString() :
                str.replace((new RegExp('\[' + map + ']', 'g')), function(a) {return map.indexOf(a)}); 
    }
    fnonblur("22");